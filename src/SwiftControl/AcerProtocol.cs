using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Web.Script.Serialization;

namespace SwiftControl
{
    internal sealed class AcerServiceClient : IDisposable
    {
        private const string KeyA = "A6052DC8A6E44210";
        private const string KeyB = "AB252AB73BED1CDB";
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private readonly int _port;
        private readonly string _question;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private TcpClient _tcp;
        private SslStream _stream;

        public AcerServiceClient(int port, string question)
        {
            _port = port;
            _question = question;
        }

        public void Connect()
        {
            _tcp = new TcpClient();
            _tcp.ReceiveTimeout = 6000;
            _tcp.SendTimeout = 6000;
            _tcp.Connect("localhost", _port);

            _stream = new SslStream(_tcp.GetStream(), false, ValidateLocalCertificate);
            _stream.ReadTimeout = 6000;
            _stream.WriteTimeout = 6000;
            _stream.AuthenticateAsClient("localhost", null, SslProtocols.Tls12, false);

            UpgradeToWebSocket();
            Authenticate();
        }

        public Dictionary<string, object> Get(string command)
        {
            return SendRequest(command, "Get", null);
        }

        public Dictionary<string, object> Set(string command, object value)
        {
            return SendRequest(command, "Set", value);
        }

        private static bool ValidateLocalCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors errors)
        {
            // The endpoint is fixed to loopback and Acer ships a private localhost
            // certificate that is intentionally not trusted by Windows.
            return certificate != null;
        }

        private void UpgradeToWebSocket()
        {
            byte[] nonce = new byte[16];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(nonce);
            }

            string key = Convert.ToBase64String(nonce);
            string request =
                "GET / HTTP/1.1\r\n" +
                "Host: localhost:" + _port.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Key: " + key + "\r\n" +
                "Sec-WebSocket-Version: 13\r\n\r\n";

            byte[] requestBytes = Encoding.ASCII.GetBytes(request);
            _stream.Write(requestBytes, 0, requestBytes.Length);
            _stream.Flush();

            string response = ReadHttpHeaders();
            if (!response.StartsWith("HTTP/1.1 101", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Acer service refused the WebSocket upgrade.");
            }

            string expectedAccept;
            using (SHA1 sha1 = SHA1.Create())
            {
                expectedAccept = Convert.ToBase64String(
                    sha1.ComputeHash(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
            }

            if (response.IndexOf("Sec-WebSocket-Accept: " + expectedAccept,
                StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException("Acer service returned an invalid WebSocket handshake.");
            }
        }

        private string ReadHttpHeaders()
        {
            using (MemoryStream buffer = new MemoryStream())
            {
                int matched = 0;
                byte[] terminator = new byte[] { 13, 10, 13, 10 };

                while (buffer.Length < 32768)
                {
                    int value = _stream.ReadByte();
                    if (value < 0)
                    {
                        throw new EndOfStreamException("Acer service closed during the WebSocket upgrade.");
                    }

                    buffer.WriteByte((byte)value);
                    if ((byte)value == terminator[matched])
                    {
                        matched++;
                        if (matched == terminator.Length)
                        {
                            return Encoding.ASCII.GetString(buffer.ToArray());
                        }
                    }
                    else
                    {
                        matched = (byte)value == terminator[0] ? 1 : 0;
                    }
                }
            }

            throw new InvalidOperationException("Acer service returned oversized HTTP headers.");
        }

        private void Authenticate()
        {
            string session = Guid.NewGuid().ToString();
            Dictionary<string, object> challenge = new Dictionary<string, object>();
            challenge["Question"] = _question;
            challenge["Key"] = KeyB;

            Dictionary<string, object> packet = new Dictionary<string, object>();
            packet["PacketType"] = 1;
            packet["Session"] = session;
            packet["Version"] = 1;
            packet["Data"] = EncryptAesEcb(_json.Serialize(challenge), KeyA);
            SendText(_json.Serialize(packet));

            Dictionary<string, object> response = ReadJsonPacket();
            if (Convert.ToInt32(response["PacketType"], CultureInfo.InvariantCulture) != 1)
            {
                throw new InvalidOperationException("Acer service returned an invalid authentication packet.");
            }

            string expected = EncryptAesEcb(_question, KeyB);
            object data;
            if (!response.TryGetValue("Data", out data) || !String.Equals(
                Convert.ToString(data, CultureInfo.InvariantCulture), expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Acer service authentication failed.");
            }
        }

        private Dictionary<string, object> SendRequest(string command, string action, object value)
        {
            string session = Guid.NewGuid().ToString();
            Dictionary<string, object> packet = new Dictionary<string, object>();
            packet["PacketType"] = 2;
            packet["Version"] = 1;
            packet["Session"] = session;
            packet["Command"] = command;
            packet["Action"] = action;
            if (value != null)
            {
                packet["Param1"] = value;
            }

            SendText(_json.Serialize(packet));

            for (int attempt = 0; attempt < 20; attempt++)
            {
                Dictionary<string, object> response = ReadJsonPacket();
                object responseSession;
                object responseCommand;
                if (response.TryGetValue("Session", out responseSession) &&
                    response.TryGetValue("Command", out responseCommand) &&
                    String.Equals(Convert.ToString(responseSession), session, StringComparison.OrdinalIgnoreCase) &&
                    String.Equals(Convert.ToString(responseCommand), command, StringComparison.Ordinal))
                {
                    object packetType;
                    if (response.TryGetValue("PacketType", out packetType) &&
                        Convert.ToInt32(packetType, CultureInfo.InvariantCulture) == 9)
                    {
                        object errorCode;
                        response.TryGetValue("ErrorCode", out errorCode);
                        throw new InvalidOperationException(
                            "Acer service rejected " + command + " (error " + Convert.ToString(errorCode) + ").");
                    }

                    return response;
                }
            }

            throw new TimeoutException("Acer service did not answer " + command + ".");
        }

        private Dictionary<string, object> ReadJsonPacket()
        {
            string message = ReadText();
            Dictionary<string, object> packet = _json.DeserializeObject(message) as Dictionary<string, object>;
            if (packet == null)
            {
                throw new InvalidOperationException("Acer service returned malformed JSON.");
            }
            return packet;
        }

        private static string EncryptAesEcb(string plaintext, string key)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = Encoding.UTF8.GetBytes(key);
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    byte[] plain = Encoding.UTF8.GetBytes(plaintext);
                    return Convert.ToBase64String(
                        encryptor.TransformFinalBlock(plain, 0, plain.Length));
                }
            }
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
                    {
                        frame.WriteByte((byte)((length >> shift) & 0xFF));
                    }
                }

                byte[] mask = new byte[4];
                using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                {
                    random.GetBytes(mask);
                }
                frame.Write(mask, 0, mask.Length);

                for (int index = 0; index < payload.Length; index++)
                {
                    frame.WriteByte((byte)(payload[index] ^ mask[index % 4]));
                }

                byte[] bytes = frame.ToArray();
                _stream.Write(bytes, 0, bytes.Length);
                _stream.Flush();
            }
        }

        private string ReadText()
        {
            using (MemoryStream message = new MemoryStream())
            {
                bool receivingText = false;
                while (true)
                {
                    int first = _stream.ReadByte();
                    int second = _stream.ReadByte();
                    if (first < 0 || second < 0)
                    {
                        throw new EndOfStreamException("Acer service closed the WebSocket.");
                    }

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
                        {
                            length = (length << 8) | extended[index];
                        }
                    }

                    if (length > 4 * 1024 * 1024)
                    {
                        throw new InvalidOperationException("Acer service returned an oversized WebSocket message.");
                    }

                    byte[] mask = masked ? ReadExact(4) : null;
                    byte[] payload = ReadExact((int)length);
                    if (masked)
                    {
                        for (int index = 0; index < payload.Length; index++)
                        {
                            payload[index] = (byte)(payload[index] ^ mask[index % 4]);
                        }
                    }

                    if (opcode >= 0x8)
                    {
                        if (!final || payload.Length > 125)
                            throw new InvalidOperationException("Acer service returned an invalid control frame.");
                        if (opcode == 0x8)
                            throw new EndOfStreamException("Acer service closed the WebSocket.");
                        if (opcode == 0x9) WriteFrame(0xA, payload);
                        else if (opcode != 0xA)
                            throw new InvalidOperationException("Acer service returned an unknown control frame.");
                        continue;
                    }

                    if (opcode == 0x1)
                    {
                        if (receivingText)
                            throw new InvalidOperationException("Acer service interrupted a fragmented message.");
                        receivingText = !final;
                    }
                    else if (opcode == 0x0)
                    {
                        if (!receivingText)
                            throw new InvalidOperationException("Acer service returned an unexpected continuation frame.");
                        receivingText = !final;
                    }
                    else
                    {
                        throw new InvalidOperationException("Acer service returned an unsupported WebSocket frame.");
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
                {
                    throw new EndOfStreamException("Acer service closed the connection.");
                }
                offset += read;
            }
            return buffer;
        }

        public void Dispose()
        {
            if (_stream != null)
            {
                try { WriteFrame(0x8, new byte[0]); }
                catch { }
                _stream.Dispose();
                _stream = null;
            }
            if (_tcp != null)
            {
                _tcp.Close();
                _tcp = null;
            }
        }
    }

    internal static class AcerJson
    {
        public static Dictionary<string, object> Result(Dictionary<string, object> packet)
        {
            object value;
            if (!packet.TryGetValue("Result", out value))
            {
                throw new InvalidOperationException("Acer service response did not contain a result.");
            }
            Dictionary<string, object> result = value as Dictionary<string, object>;
            if (result == null)
                throw new InvalidOperationException("Acer service returned an invalid result.");
            return result;
        }

        public static int Int(Dictionary<string, object> data, string key, int fallback)
        {
            object value;
            return data.TryGetValue(key, out value)
                ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
                : fallback;
        }

        public static bool Bool(Dictionary<string, object> data, string key, bool fallback)
        {
            object value;
            return data.TryGetValue(key, out value)
                ? Convert.ToBoolean(value, CultureInfo.InvariantCulture)
                : fallback;
        }

        public static string Text(Dictionary<string, object> data, string key, string fallback)
        {
            object value;
            return data.TryGetValue(key, out value)
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : fallback;
        }

        public static IEnumerable Dictionaries(object value)
        {
            IEnumerable items = value as IEnumerable;
            return items ?? new object[0];
        }
    }
}
