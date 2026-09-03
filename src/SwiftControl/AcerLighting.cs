using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace SwiftControl
{
    internal static class AcerLightingEffects
    {
        public const string Blink = "Blink";
        public const string Breath = "Breath";
        public const string Circle = "Circle_R";
        public const string Twinkle = "Twinkle_R";

        public static string Normalize(string effect)
        {
            string[] supported = { Blink, Breath, Circle, Twinkle };
            foreach (string candidate in supported)
            {
                if (String.Equals(candidate, effect, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            throw new ArgumentException("Unsupported Acer lighting effect: " + effect, "effect");
        }
    }

    internal sealed class AcerLightingClient : IDisposable
    {
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private static readonly int[] Ports =
        {
            55995, 55996, 55997, 55998, 55999,
            56955, 56956, 56957, 56958, 56959
        };

        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        internal static readonly object Synchronization = new object();
        private TcpClient _tcp;
        private NetworkStream _stream;

        public int Port { get; private set; }

        public void Connect()
        {
            if (_stream != null) return;

            Exception lastError = null;
            foreach (int port in Ports)
            {
                try
                {
                    ConnectPort(port);
                    GetDevices();
                    return;
                }
                catch (Exception exception)
                {
                    lastError = exception;
                    Disconnect();
                }
            }

            throw new InvalidOperationException(
                "No authorized Acer lighting service responded.", lastError);
        }

        public int[] GetDevices()
        {
            Dictionary<string, object> response = SendRequest(
                "GET_ULTRON_LIGHTING_CAPABILITY", null);
            Dictionary<string, object> data = GetDictionary(response, "data");
            object devicesValue;
            IEnumerable devices = data.TryGetValue("devices", out devicesValue)
                ? devicesValue as IEnumerable
                : null;
            List<int> result = new List<int>();
            if (devices != null)
            {
                foreach (object device in devices)
                    result.Add(Convert.ToInt32(device, CultureInfo.InvariantCulture));
            }
            return result.ToArray();
        }

        public bool GetEnabled(int deviceId)
        {
            Dictionary<string, object> parameter = new Dictionary<string, object>();
            parameter["id"] = deviceId;
            Dictionary<string, object> response = SendRequest(
                "GET_ULTRON_LIGHTING_STATUS", parameter);
            Dictionary<string, object> data = GetDictionary(response, "data");
            object status;
            return data.TryGetValue("status", out status) &&
                Convert.ToInt32(status, CultureInfo.InvariantCulture) == 1;
        }

        public void SetEnabled(int deviceId, bool enabled)
        {
            Dictionary<string, object> parameter = new Dictionary<string, object>();
            parameter["id"] = deviceId;
            parameter["status"] = enabled ? 1 : 0;
            SendRequest("SET_ULTRON_LIGHTING_STATUS", parameter);
        }

        public void PlayEffect(string effect)
        {
            Dictionary<string, object> parameter = new Dictionary<string, object>();
            parameter["effect"] = AcerLightingEffects.Normalize(effect);
            SendRequest("SET_ULTRON_LIGHTING_EFFECT", parameter);
        }

        public void TerminateEffect()
        {
            SendRequest("TERMINATE_ULTRON_LIGHTING_EFFECT", null);
        }

        private void ConnectPort(int port)
        {
            _tcp = new TcpClient();
            _tcp.ReceiveTimeout = 2500;
            _tcp.SendTimeout = 2500;
            _tcp.Connect("localhost", port);
            _stream = _tcp.GetStream();
            _stream.ReadTimeout = 2500;
            _stream.WriteTimeout = 2500;
            Port = port;
            UpgradeToWebSocket();
        }

        private void UpgradeToWebSocket()
        {
            byte[] nonce = new byte[16];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                random.GetBytes(nonce);

            string key = Convert.ToBase64String(nonce);
            string request =
                "GET / HTTP/1.1\r\n" +
                "Host: localhost:" + Port.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Key: " + key + "\r\n" +
                "Sec-WebSocket-Version: 13\r\n\r\n";

            byte[] requestBytes = Encoding.ASCII.GetBytes(request);
            _stream.Write(requestBytes, 0, requestBytes.Length);
            _stream.Flush();

            string response = ReadHttpHeaders();
            if (!response.StartsWith("HTTP/1.1 101", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Acer lighting service refused the WebSocket upgrade.");

            string expectedAccept;
            using (SHA1 sha1 = SHA1.Create())
            {
                expectedAccept = Convert.ToBase64String(
                    sha1.ComputeHash(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
            }
            if (response.IndexOf("Sec-WebSocket-Accept: " + expectedAccept,
                StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Acer lighting service returned an invalid handshake.");
        }

        private string ReadHttpHeaders()
        {
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] terminator = { 13, 10, 13, 10 };
                int matched = 0;
                while (buffer.Length < 32768)
                {
                    int value = _stream.ReadByte();
                    if (value < 0)
                        throw new EndOfStreamException("Acer lighting service closed during handshake.");
                    buffer.WriteByte((byte)value);
                    if ((byte)value == terminator[matched])
                    {
                        matched++;
                        if (matched == terminator.Length)
                            return Encoding.ASCII.GetString(buffer.ToArray());
                    }
                    else
                    {
                        matched = (byte)value == terminator[0] ? 1 : 0;
                    }
                }
            }
            throw new InvalidOperationException("Acer lighting service returned oversized headers.");
        }

        private Dictionary<string, object> SendRequest(
            string function, Dictionary<string, object> parameter)
        {
            Dictionary<string, object> request = new Dictionary<string, object>();
            request["Function"] = function;
            if (parameter != null) request["Parameter"] = parameter;
            SendText("ACER" + _json.Serialize(request));

            for (int attempt = 0; attempt < 10; attempt++)
            {
                string text = ReadText().Trim('\0', ' ', '\r', '\n', '\t');
                if (String.Equals(text, "Allowed Client", StringComparison.Ordinal))
                    continue;

                Dictionary<string, object> response =
                    _json.DeserializeObject(text) as Dictionary<string, object>;
                if (response == null) continue;

                object requestValue;
                if (!response.TryGetValue("request", out requestValue) ||
                    !String.Equals(Convert.ToString(requestValue), function,
                        StringComparison.Ordinal))
                    continue;

                object resultValue;
                int result = response.TryGetValue("result", out resultValue)
                    ? Convert.ToInt32(resultValue, CultureInfo.InvariantCulture)
                    : -1;
                if (result != 0)
                    throw new InvalidOperationException(
                        "Acer lighting service rejected " + function + " (result " +
                        result.ToString(CultureInfo.InvariantCulture) + ").");
                return response;
            }

            throw new TimeoutException("Acer lighting service did not answer " + function + ".");
        }

        private static Dictionary<string, object> GetDictionary(
            Dictionary<string, object> source, string key)
        {
            object value;
            Dictionary<string, object> result = source.TryGetValue(key, out value)
                ? value as Dictionary<string, object>
                : null;
            if (result == null)
                throw new InvalidOperationException("Acer lighting response omitted " + key + ".");
            return result;
        }

        private void SendText(string text)
        {
            WriteFrame(0x1, Encoding.UTF8.GetBytes(text));
        }

        private void WriteFrame(byte opcode, byte[] payload)
        {
            using (MemoryStream frame = new MemoryStream())
            {
                frame.WriteByte((byte)(0x80 | opcode));
                if (payload.Length < 126)
                {
                    frame.WriteByte((byte)(0x80 | payload.Length));
                }
                else if (payload.Length <= UInt16.MaxValue)
                {
                    frame.WriteByte(0xFE);
                    frame.WriteByte((byte)((payload.Length >> 8) & 0xFF));
                    frame.WriteByte((byte)(payload.Length & 0xFF));
                }
                else
                {
                    frame.WriteByte(0xFF);
                    ulong length = (ulong)payload.LongLength;
                    for (int shift = 56; shift >= 0; shift -= 8)
                        frame.WriteByte((byte)((length >> shift) & 0xFF));
                }

                byte[] mask = new byte[4];
                using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                    random.GetBytes(mask);
                frame.Write(mask, 0, mask.Length);
                for (int index = 0; index < payload.Length; index++)
                    frame.WriteByte((byte)(payload[index] ^ mask[index % 4]));

                byte[] bytes = frame.ToArray();
                _stream.Write(bytes, 0, bytes.Length);
                _stream.Flush();
            }
        }

        private string ReadText()
        {
            using (MemoryStream message = new MemoryStream())
            {
                bool receiving = false;
                while (true)
                {
                    int first = _stream.ReadByte();
                    int second = _stream.ReadByte();
                    if (first < 0 || second < 0)
                        throw new EndOfStreamException("Acer lighting service closed the WebSocket.");

                    bool final = (first & 0x80) != 0;
                    int opcode = first & 0x0F;
                    bool masked = (second & 0x80) != 0;
                    ulong length = (ulong)(second & 0x7F);
                    if (length == 126)
                    {
                        byte[] extended = ReadExact(2);
                        length = (ulong)((extended[0] << 8) | extended[1]);
                    }
                    else if (length == 127)
                    {
                        byte[] extended = ReadExact(8);
                        length = 0;
                        for (int index = 0; index < 8; index++)
                            length = (length << 8) | extended[index];
                    }
                    if (length > 4 * 1024 * 1024)
                        throw new InvalidOperationException("Acer lighting response was oversized.");

                    byte[] mask = masked ? ReadExact(4) : null;
                    byte[] payload = ReadExact((int)length);
                    if (masked)
                    {
                        for (int index = 0; index < payload.Length; index++)
                            payload[index] = (byte)(payload[index] ^ mask[index % 4]);
                    }

                    if (opcode >= 0x8)
                    {
                        if (opcode == 0x8)
                            throw new EndOfStreamException("Acer lighting service closed the WebSocket.");
                        if (opcode == 0x9) WriteFrame(0xA, payload);
                        continue;
                    }

                    if (opcode == 0x1 || opcode == 0x2)
                    {
                        if (receiving)
                            throw new InvalidOperationException("Acer lighting fragmented-message error.");
                        receiving = !final;
                    }
                    else if (opcode == 0x0)
                    {
                        if (!receiving)
                            throw new InvalidOperationException("Unexpected lighting continuation frame.");
                        receiving = !final;
                    }
                    else
                    {
                        throw new InvalidOperationException("Unsupported Acer lighting WebSocket frame.");
                    }

                    message.Write(payload, 0, payload.Length);
                    if (final) return Encoding.UTF8.GetString(message.ToArray());
                }
            }
        }

        private byte[] ReadExact(int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = _stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                    throw new EndOfStreamException("Acer lighting service closed the connection.");
                offset += read;
            }
            return buffer;
        }

        private void Disconnect()
        {
            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }
            if (_tcp != null)
            {
                _tcp.Close();
                _tcp = null;
            }
            Port = 0;
        }

        public void Dispose()
        {
            if (_stream != null)
            {
                try { WriteFrame(0x8, new byte[0]); }
                catch { }
            }
            Disconnect();
        }
    }
}
