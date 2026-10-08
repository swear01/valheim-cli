using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Xml;

namespace ValheimCliBridge
{
    [DataContract]
    public sealed class Request
    {
        [DataMember(IsRequired = true)] public string id;
        [DataMember(IsRequired = true)] public string token;
        [DataMember(IsRequired = true)] public string operation;
        [DataMember(EmitDefaultValue = false)] public double? x;
        [DataMember(EmitDefaultValue = false)] public double? y;
        [DataMember(EmitDefaultValue = false)] public double? z;

        public void Validate()
        {
            if (!Guid.TryParseExact(id, "D", out _) || token == null || token.Length != 64)
                throw new InvalidDataException("Invalid request schema");
            if (operation != "status" && operation != "players" && operation != "teleport")
                throw new InvalidDataException("Unknown operation");
            if (operation == "teleport")
            {
                if (!Finite(x) || !Finite(y) || !Finite(z) || Math.Abs(x.Value) > 10500 || Math.Abs(z.Value) > 10500 || y < -1000 || y > 5000)
                    throw new InvalidDataException("Invalid coordinates");
            }
            else if (x.HasValue || y.HasValue || z.HasValue) throw new InvalidDataException("Unexpected coordinates");
        }
        private static bool Finite(double? value) => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);
    }

    [DataContract]
    public sealed class Position
    {
        [DataMember] public float x;
        [DataMember] public float y;
        [DataMember] public float z;
    }

    [DataContract]
    public sealed class Response
    {
        [DataMember] public string id;
        [DataMember] public bool ok;
        [DataMember(EmitDefaultValue = false)] public string error;
        [DataMember(EmitDefaultValue = false)] public string state;
        [DataMember(EmitDefaultValue = false)] public string version;
        [DataMember] public bool inWorld;
        [DataMember] public bool host;
        [DataMember] public bool teleportAllowed;
        [DataMember] public bool teleporting;
        [DataMember(EmitDefaultValue = false)] public string world;
        [DataMember(EmitDefaultValue = false)] public string player;
        [DataMember(EmitDefaultValue = false)] public string[] players;
        [DataMember(EmitDefaultValue = false)] public Position position;
        [DataMember(EmitDefaultValue = false)] public Position destination;
        public static Response Error(string id, string message, string state = null) => new Response { id = id, ok = false, error = message, state = state };
    }

    public static class Protocol
    {
        public const int MaxFrame = 65536;
        public static byte[] Encode<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return stream.ToArray();
            }
        }
        public static Request Decode(byte[] bytes)
        {
            var fields = new HashSet<string>();
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(bytes, new XmlDictionaryReaderQuotas { MaxDepth = 4, MaxStringContentLength = 512 }))
            {
                reader.MoveToContent();
                if (reader.Name != "root" || reader.GetAttribute("type") != "object" || reader.GetAttribute("__type") != null) throw new InvalidDataException("Expected plain request object");
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    if (reader.Depth != 1 || !fields.Add(reader.Name) ||
                        (reader.Name != "id" && reader.Name != "token" && reader.Name != "operation" && reader.Name != "x" && reader.Name != "y" && reader.Name != "z"))
                        throw new InvalidDataException("Invalid request fields");
                    var kind = reader.GetAttribute("type");
                    var coordinate = reader.Name == "x" || reader.Name == "y" || reader.Name == "z";
                    if (kind != (coordinate ? "number" : "string")) throw new InvalidDataException("Invalid request field type");
                }
            }
            using (var stream = new MemoryStream(bytes))
            {
                var value = (Request)new DataContractJsonSerializer(typeof(Request)).ReadObject(stream);
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing data");
                value.Validate();
                return value;
            }
        }
        public static byte[] ReadFrame(Stream stream)
        {
            var header = ReadExact(stream, 4);
            var length = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
            if (length == 0 || length > MaxFrame) throw new InvalidDataException("Invalid frame length");
            return ReadExact(stream, (int)length);
        }
        private static byte[] ReadExact(Stream stream, int length)
        {
            var bytes = new byte[length];
            for (int offset = 0; offset < length;)
            {
                var read = stream.Read(bytes, offset, length - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return bytes;
        }
        public static void WriteFrame(Stream stream, Response response)
        {
            var bytes = Encode(response);
            if (bytes.Length > MaxFrame) throw new InvalidDataException("Response too large");
            stream.Write(new byte[] { (byte)(bytes.Length >> 24), (byte)(bytes.Length >> 16), (byte)(bytes.Length >> 8), (byte)bytes.Length }, 0, 4);
            stream.Write(bytes, 0, bytes.Length);
        }
        public static bool Authenticated(string expected, string actual)
        {
            if (expected == null || actual == null || expected.Length != 64 || actual.Length != 64) return false;
            int difference = 0;
            for (int i = 0; i < 64; i++) difference |= expected[i] ^ actual[i];
            return difference == 0;
        }
        public static string CreateToken()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
