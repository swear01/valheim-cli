using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ValheimCliBridge
{
    public sealed class Server : IDisposable
    {
        private readonly TcpListener listener;
        private readonly Dispatcher dispatcher;
        private readonly string token;
        private readonly Action<Exception> logError;
        private readonly Func<string, Response> stopInput;
        private readonly Func<long> inputEpoch;
        private readonly Thread thread;
        private volatile bool stopped;
        private TcpClient active;
        private readonly object gate = new object();
        private readonly HashSet<string> writes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public Server(int port, string token, Dispatcher dispatcher, Action<Exception> logError, Func<string, Response> stopInput = null, Func<long> inputEpoch = null)
        {
            if (token == null || token.Length != 64) throw new ArgumentException("Invalid token");
            this.token = token;
            this.dispatcher = dispatcher;
            this.logError = logError;
            this.stopInput = stopInput;
            this.inputEpoch = inputEpoch;
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start(8);
            thread = new Thread(Listen) { IsBackground = true, Name = "ValheimCliBridge" };
            thread.Start();
        }
        private void Listen()
        {
            while (!stopped)
            {
                try
                {
                    using (var client = listener.AcceptTcpClient())
                    using (var deadline = new System.Threading.Timer(_ => client.Close(), null, 4000, Timeout.Infinite))
                    {
                        lock (gate) { if (stopped) return; active = client; }
                        client.ReceiveTimeout = 1000;
                        client.SendTimeout = 1000;
                        using (var stream = client.GetStream())
                        {
                            Response response;
                            try
                            {
                                var request = Protocol.Decode(Protocol.ReadFrame(stream));
                                if (!Protocol.Authenticated(token, request.token)) response = Response.Error(request.id, "Unauthorized");
                                else if (request.operation == "stop" && stopInput != null) response = stopInput(request.id);
                                else if (request.IsWrite && writes.Contains(request.id)) response = Response.Error(request.id, "Write ID already used; inspect status, do not retry");
                                else if (request.IsWrite && writes.Count >= 65536) response = Response.Error(request.id, "Write limit reached; restart the bridge before a new session");
                                else
                                {
                                    if (request.IsControl && inputEpoch != null) request.controlEpoch = inputEpoch();
                                    if (request.IsWrite) writes.Add(request.id);
                                    response = dispatcher.Run(request);
                                    if (request.IsWrite && response.state == "cancelled") writes.Remove(request.id);
                                }
                            }
                            catch { response = Response.Error(null, "Invalid request"); }
                            Protocol.WriteFrame(stream, response);
                        }
                        lock (gate) active = null;
                    }
                }
                catch (IOException) { }
                catch (InvalidDataException) { }
                catch (SerializationException) { }
                catch (SocketException) { if (stopped) return; }
                catch (ObjectDisposedException) { if (stopped) return; }
                catch (Exception error) { if (stopped) return; logError(error); }
            }
        }
        public void Dispose()
        {
            stopped = true;
            dispatcher.Stop();
            listener.Stop();
            lock (gate) active?.Close();
            thread.Join(3000);
        }
    }
}
