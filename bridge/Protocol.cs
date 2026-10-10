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
        [DataMember(EmitDefaultValue = false)] public string actions;
        [DataMember(EmitDefaultValue = false)] public double? moveX;
        [DataMember(EmitDefaultValue = false)] public double? moveZ;
        [DataMember(EmitDefaultValue = false)] public int? durationMs;
        [DataMember(EmitDefaultValue = false)] public double? yaw;
        [DataMember(EmitDefaultValue = false)] public double? pitch;
        [DataMember(EmitDefaultValue = false)] public string action;
        [DataMember(EmitDefaultValue = false)] public int? slot;
        [DataMember(EmitDefaultValue = false)] public string uiAction;
        [DataMember(EmitDefaultValue = false)] public string button;
        [DataMember(EmitDefaultValue = false)] public int? scroll;
        [DataMember(EmitDefaultValue = false)] public double? pointerX;
        [DataMember(EmitDefaultValue = false)] public double? pointerY;
        internal long? controlEpoch;
        public bool IsControl => operation == "input" || operation == "look" || operation == "action" || operation == "ui";
        public bool IsWrite => operation == "teleport" || IsControl;

        public void Validate()
        {
            if (!Guid.TryParseExact(id, "D", out _) || token == null || token.Length != 64)
                throw new InvalidDataException("Invalid request schema");
            if (operation != "status" && operation != "players" && operation != "teleport" && operation != "observe" && !IsControl && operation != "stop")
                throw new InvalidDataException("Unknown operation");
            if (operation == "teleport")
            {
                if (!Finite(x) || !Finite(y) || !Finite(z) || Math.Abs(x.Value) > 10500 || Math.Abs(z.Value) > 10500 || y < -1000 || y > 5000)
                    throw new InvalidDataException("Invalid coordinates");
            }
            else if (x.HasValue || y.HasValue || z.HasValue) throw new InvalidDataException("Unexpected coordinates");
            if (operation == "input") InputSpec.Validate(this);
            else if (actions != null || moveX.HasValue || moveZ.HasValue || durationMs.HasValue)
                throw new InvalidDataException("Unexpected input fields");
            if (operation == "look")
            {
                if (!yaw.HasValue && !pitch.HasValue || !InputSpec.Bounded(yaw ?? 0, 180) || !InputSpec.Bounded(pitch ?? 0, 180))
                    throw new InvalidDataException("Look requires finite yaw/pitch deltas in -180..180 degrees");
            }
            else if (yaw.HasValue || pitch.HasValue) throw new InvalidDataException("Unexpected look fields");
            if (operation == "action")
            {
                if (action != "interact" && action != "slot" && action != "inventory" && action != "build-menu" && action != "hide" && action != "guardian" && action != "place" && action != "rotate")
                    throw new InvalidDataException("Unknown game action");
                if (action == "slot" ? !slot.HasValue || slot < 1 || slot > 8 : slot.HasValue) throw new InvalidDataException("Slot requires 1..8 and only applies to the slot action");
                if (action == "rotate" ? !scroll.HasValue || scroll == 0 || Math.Abs((long)scroll.Value) > 10 : scroll.HasValue) throw new InvalidDataException("Rotate requires nonzero scroll in -10..10");
            }
            else if (action != null || slot.HasValue) throw new InvalidDataException("Unexpected game action fields");
            if (operation == "ui")
            {
                if (uiAction != "click" && uiAction != "scroll") throw new InvalidDataException("Unknown UI action");
                if (!pointerX.HasValue || !pointerY.HasValue || !Finite(pointerX) || !Finite(pointerY) || pointerX < 0 || pointerX > 1 || pointerY < 0 || pointerY > 1) throw new InvalidDataException("UI requires normalized pointer x/y in 0..1");
                if (button != null && button != "left" && button != "right" && button != "middle") throw new InvalidDataException("Unknown UI button");
                if (uiAction == "scroll" ? !scroll.HasValue || scroll == 0 || Math.Abs((long)scroll.Value) > 10 : scroll.HasValue) throw new InvalidDataException("Scroll requires nonzero -10..10");
            }
            else if (uiAction != null || button != null || pointerX.HasValue || pointerY.HasValue || operation != "action" && scroll.HasValue) throw new InvalidDataException("Unexpected UI fields");
        }
        private static bool Finite(double? value) => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value);
    }

    [DataContract]
    public sealed class InventoryItem
    {
        [DataMember] public string name;
        [DataMember] public int count;
        [DataMember] public int quality;
        [DataMember] public int column;
        [DataMember] public int row;
        [DataMember] public bool equipped;
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
        [DataMember] public bool controlAllowed;
        [DataMember] public bool inputActive;
        [DataMember(EmitDefaultValue = false)] public string inputState;
        [DataMember(EmitDefaultValue = false)] public string inputId;
        [DataMember] public bool foreground;
        [DataMember] public bool dead;
        [DataMember] public float health;
        [DataMember] public float maxHealth;
        [DataMember] public float stamina;
        [DataMember] public float maxStamina;
        [DataMember(EmitDefaultValue = false)] public Position view;
        [DataMember(EmitDefaultValue = false)] public InventoryItem[] inventory;
        [DataMember(EmitDefaultValue = false)] public string image;
        [DataMember] public int imageWidth;
        [DataMember] public int imageHeight;
        [DataMember(EmitDefaultValue = false)] public string capturedAt;
        [DataMember(EmitDefaultValue = false)] public string captureError;
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
        public const int MaxObservationFrame = 8 * 1024 * 1024;
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
                    var textField = reader.Name == "id" || reader.Name == "token" || reader.Name == "operation" || reader.Name == "actions" || reader.Name == "action" || reader.Name == "uiAction" || reader.Name == "button";
                    var numberField = reader.Name == "x" || reader.Name == "y" || reader.Name == "z" || reader.Name == "durationMs" || reader.Name == "moveX" || reader.Name == "moveZ" || reader.Name == "yaw" || reader.Name == "pitch" || reader.Name == "slot" || reader.Name == "scroll" || reader.Name == "pointerX" || reader.Name == "pointerY";
                    if (reader.Depth != 1 || !fields.Add(reader.Name) || (!textField && !numberField))
                        throw new InvalidDataException("Invalid request fields");
                    var kind = reader.GetAttribute("type");
                    if (kind != (numberField ? "number" : "string")) throw new InvalidDataException("Invalid request field type");
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
            if (bytes.Length > (response.image == null ? MaxFrame : MaxObservationFrame)) throw new InvalidDataException("Response too large");
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
