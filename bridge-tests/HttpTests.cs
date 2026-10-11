using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ValheimCliBridge;

static class HttpTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    internal static async Task Run()
    {
        var runs = 0;
        var stops = 0;
        var logs = 0;
        var token = new string('a', 64);
        var dispatcher = new Dispatcher(request => {
            runs++;
            return new Response { id = request.id, ok = true, state = request.command,
                image = request.operation == "observe" ? new string('A', 100000) : null };
        });
        using var server = new Server(0, token, dispatcher, _ => logs++, id => { stops++; return new Response { id = id, ok = true, state = "stopped" }; });
        using var handler = new HttpClientHandler { UseProxy = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var url = "http://127.0.0.1:" + server.Port + "/v1/command";
        Request Request(string operation = "status") => new Request { id = Guid.NewGuid().ToString(), token = token, operation = operation };
        async Task<T> Pump<T>(Task<T> task)
        {
            var until = DateTime.UtcNow.AddSeconds(6);
            while (!task.IsCompleted && DateTime.UtcNow < until) { dispatcher.Pump(); await Task.Delay(1); }
            Assert(task.IsCompleted, "HTTP test timed out");
            return await task;
        }
        async Task<JsonElement> Post(Request request, bool expect = false, int status = 200)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, url);
            message.Headers.ExpectContinue = expect;
            message.Content = new ByteArrayContent(Protocol.Encode(request));
            message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            using var response = await Pump(http.SendAsync(message));
            Assert((int)response.StatusCode == status, "Unexpected HTTP response status");
            Assert(response.Headers.CacheControl.NoStore, "HTTP response cacheable");
            var result = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync()).RootElement.Clone();
            Assert(result.GetProperty("id").GetString() == request.id, "HTTP request identity lost");
            return result;
        }
        async Task<JsonElement> Tcp(Request request)
        {
            using var client = new TcpClient("127.0.0.1", server.Port);
            using var stream = client.GetStream();
            var bytes = Protocol.Encode(request);
            stream.Write(new byte[] { 0, 0, (byte)(bytes.Length >> 8), (byte)bytes.Length });
            stream.Write(bytes);
            var reply = Task.Run(() => Protocol.ReadFrame(stream));
            return JsonDocument.Parse(await Pump(reply)).RootElement.Clone();
        }
        async Task Reject(string header, byte[] body = null, bool fragment = false)
        {
            var before = runs;
            using var client = new TcpClient("127.0.0.1", server.Port);
            using var stream = client.GetStream();
            var bytes = Encoding.ASCII.GetBytes(header);
            if (fragment) foreach (var b in bytes) stream.WriteByte(b); else stream.Write(bytes);
            if (body != null) stream.Write(body);
            client.Client.Shutdown(SocketShutdown.Send);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var reply = await Pump(reader.ReadLineAsync());
            Assert(reply.StartsWith("HTTP/1.1 400 "), "Unsafe HTTP request accepted: " + header.Split('\r')[0]);
            Assert(runs == before, "Invalid HTTP request reached dispatcher");
        }
        Assert((await Post(Request())).GetProperty("ok").GetBoolean(), "HTTP status failed");
        var wrong = Request(); wrong.token = new string('b', 64);
        Assert(!(await Post(wrong, status: 401)).GetProperty("ok").GetBoolean(), "HTTP authentication bypassed");
        var command = Request("console"); command.command = "say 藍莓";
        Assert((await Post(command, expect: true)).GetProperty("state").GetString() == command.command, "UTF-8/100-continue failed");
        Assert((await Tcp(command)).GetProperty("error").GetString().Contains("already used"), "HTTP-to-TCP replay accepted");
        command.id = Guid.NewGuid().ToString();
        Assert((await Tcp(command)).GetProperty("ok").GetBoolean(), "Legacy TCP broken");
        command.id = command.id.ToUpperInvariant();
        Assert((await Post(command)).GetProperty("error").GetString().Contains("already used"), "TCP-to-HTTP replay accepted");
        Assert((await Post(Request("observe"))).GetProperty("image").GetString().Length == 100000, "HTTP observation capped at 64 KiB");
        Assert((await Post(Request("stop"))).GetProperty("state").GetString() == "stopped" && stops == 1, "HTTP emergency stop queued");
        var requestBody = Protocol.Encode(Request());
        var head = "POST /v1/command HTTP/1.1\r\nHost: 127.0.0.1:" + server.Port + "\r\nContent-Type: application/json\r\nContent-Length: " + requestBody.Length;
        foreach (var bad in new[] {
            head.Replace("POST", "GET"), head.Replace("/v1/command", "/v1/command?token=x"),
            head.Replace("HTTP/1.1", "HTTP/1.0"), head.Replace("127.0.0.1", "attacker.example"),
            head.Replace("application/json", "text/plain"), head.Replace("Content-Length:", "Content-Length :"),
            head + "\r\nOrigin: http://attacker.example", head + "\r\nTransfer-Encoding: chunked",
            head + "\r\nContent-Length: 1", head + "\r\nExpect: unknown", head + "\r\nX: a\nb",
            head.Replace("Content-Length: " + requestBody.Length, "Content-Length: 65537"),
            head.Replace("Content-Length: " + requestBody.Length, "Content-Length: -1"),
            head.Replace("Content-Length: " + requestBody.Length, "Content-Length: 0"),
            head.Replace("Content-Length: " + requestBody.Length, "Missing-Length: 1"),
            (head + "\r\nX: " + new string('a', 8200)).Substring(0, 8188), head + "\r\n folded: header"
        }) await Reject(bad + "\r\n\r\n", fragment: true);
        await Reject(head + "\r\n\r\n", requestBody[..^1]);
        await Reject(head + "\r\n\r\n", Enumerable.Repeat((byte)'x', requestBody.Length).ToArray());
        Assert((await Post(Request())).GetProperty("ok").GetBoolean(), "Malformed HTTP killed listener");
        Assert(logs == 0, "HTTP test caused unexpected server exception");
        System.Console.WriteLine("PASS: HTTP JSON, auth, UTF-8/100-continue, shared TCP replay protection, observation, emergency stop, malformed framing/origin/host/size rejection");
    }
}
