using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ValheimCliBridge
{
    // One bounded JSON POST per connection; shares the existing loopback listener and dispatcher.
    internal static class HttpTransport
    {
        internal static byte[] Read(Stream stream, byte[] prefix, int port)
        {
            var header = new List<byte>(prefix);
            while (header.Count < 8192)
            {
                var value = stream.ReadByte();
                if (value < 0) throw new EndOfStreamException();
                header.Add((byte)value);
                var count = header.Count;
                if (count >= 4 && header[count - 4] == 13 && header[count - 3] == 10 && header[count - 2] == 13 && value == 10) break;
            }
            if (header.Count >= 8192) throw new InvalidDataException("HTTP headers too large");
            foreach (var value in header)
                if (value > 126 || value < 32 && value != 13 && value != 10 && value != 9)
                    throw new InvalidDataException("Invalid HTTP header encoding");
            var lines = Encoding.ASCII.GetString(header.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines[0] != "POST /v1/command HTTP/1.1") throw new InvalidDataException("Expected POST /v1/command HTTP/1.1");
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < lines.Length - 2; i++)
            {
                var colon = lines[i].IndexOf(':');
                if (colon <= 0) throw new InvalidDataException("Invalid HTTP header");
                var name = lines[i].Substring(0, colon);
                foreach (var c in name)
                    if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || "!#$%&'*+-.^_`|~".IndexOf(c) >= 0))
                        throw new InvalidDataException("Invalid HTTP field name");
                if (fields.ContainsKey(name)) throw new InvalidDataException("Duplicate HTTP header");
                var value = lines[i].Substring(colon + 1).Trim(' ', '\t');
                if (value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0) throw new InvalidDataException("Invalid HTTP field value");
                fields.Add(name, value);
            }
            if (!fields.TryGetValue("Host", out var host) || host != "127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) ||
                fields.ContainsKey("Origin") || fields.ContainsKey("Transfer-Encoding"))
                throw new InvalidDataException("Only local non-browser requests with Content-Length are supported");
            if (!fields.TryGetValue("Content-Type", out var type)) throw new InvalidDataException("Expected application/json");
            var media = type.Split(';');
            if (!media[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase) || media.Length > 2 ||
                media.Length == 2 && !media[1].Trim().Equals("charset=utf-8", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Expected UTF-8 application/json");
            if (!fields.TryGetValue("Content-Length", out var lengthText) || !int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length <= 0 || length > Protocol.MaxFrame)
                throw new InvalidDataException("Invalid HTTP body length");
            if (fields.TryGetValue("Expect", out var expect))
            {
                if (!expect.Equals("100-continue", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsupported HTTP expectation");
                var interim = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                stream.Write(interim, 0, interim.Length);
                stream.Flush();
            }
            return Protocol.ReadExact(stream, length);
        }
        internal static void Write(Stream stream, Response response, int status)
        {
            var bytes = Protocol.EncodeResponse(response);
            var reason = status == 200 ? "OK" : status == 401 ? "Unauthorized" : "Bad Request";
            var header = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n\r\n");
            stream.Write(header, 0, header.Length);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
