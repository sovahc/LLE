using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ProtoBuf;

namespace LLE.Server
{
    class Program
    {
        static readonly HttpClient _http = new HttpClient();
        const string LlmUrl = "http://localhost:8080/v1/chat/completions";

        static void Main(string[] args)
        {
            var listener = new TcpListener(IPAddress.Loopback, 8081);
            listener.Start();
            Console.WriteLine("Listening on 127.0.0.1:8081");

            while (true)
            {
                var client = listener.AcceptTcpClient();
                Console.WriteLine("Client connected.");
                HandleClient(client);
            }
        }

        static async void HandleClient(TcpClient client)
        {
            var stream = client.GetStream();
            var chatBuffer = new Queue<string>();

            try
            {
                while (true)
                {
                    // 1. Read 2 bytes length
                    byte[] lenBuf = new byte[2];
                    await ReadExactlyAsync(stream, lenBuf);
                    int length = lenBuf[0] | (lenBuf[1] << 8);

                    // 2. Read 1 byte type
                    byte[] typeBuf = new byte[1];
                    await ReadExactlyAsync(stream, typeBuf);
                    int msgType = typeBuf[0];

                    // 3. Read payload
                    byte[] payload = new byte[length];
                    await ReadExactlyAsync(stream, payload);

                    // 4. Dispatch
                    switch (msgType)
                    {
                        case 0: // Vision
                            var state = Serializer.Deserialize<LLE.LastKnownState>(new MemoryStream(payload));
                            Console.WriteLine($"[VISION] X={state.X:F2} Y={state.Y:F2} Z={state.Z:F2}");
                            break;

                        case 1: // Chat
                            var chat = Serializer.Deserialize<LLE.ChatMessage>(new MemoryStream(payload));
                            Console.WriteLine($"[CHAT] {chat.Author}: {chat.Text}");
                            chatBuffer.Enqueue($"{chat.Author}: {chat.Text}");
                            if (chatBuffer.Count > 50) chatBuffer.Dequeue();

                            string context = "\n" + string.Join("\n", chatBuffer);
                            string llmReply = await AskLlm(context);
                            Console.WriteLine($"[LLM] {llmReply}");

                            SendFrame(stream, 2, new LLE.ServerCommand {
                                CommandType = 0,
                                Payload = llmReply
                            });
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Client disconnected: " + ex.Message);
            }
            finally
            {
                client.Close();
            }
        }

        static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\")
                     .Replace("\"", "\\\"")
                     .Replace("\n", "\\n")
                     .Replace("\r", "\\r");
        }

        static async Task<string> AskLlm(string chatContext)
        {
            string safeContext = EscapeJson(chatContext);
            string body = "{ \"model\": \"qwen\", \"messages\": [ "
                + "{\"role\":\"system\",\"content\":\"Reply max 50 characters. No explanations.\"}, "
                + $"{{\"role\":\"user\",\"content\":\"Chat history:{safeContext}\"}} "
                + "], \"max_tokens\": 64, \"stream\": false }";

            var response = await _http.PostAsync(LlmUrl, new StringContent(body, Encoding.UTF8, "application/json"));
            string text = await response.Content.ReadAsStringAsync();

            // Extract content from choices[0].message.content via simple substring search
            int idx = text.IndexOf("\"content\":\"");
            if (idx < 0) return "";
            idx += 11; // skip '"content":"'
            var sb = new StringBuilder();
            while (idx < text.Length)
            {
                char c = text[idx++];
                if (c == '\\') {
                    char next = text[idx++];
                    sb.Append(next == '"' ? '"' : next == '/' ? '/' : next == 'n' ? '\n' : next);
                } else if (c == '"') break;
                else sb.Append(c);
            }

            return sb.ToString().Trim();
        }

        static void SendFrame<T>(NetworkStream stream, int type, T obj)
        {
            byte[] payload;
            using (var ms = new MemoryStream())
            {
                Serializer.Serialize(ms, obj);
                payload = ms.ToArray();
            }
            int len = payload.Length;
            stream.WriteByte((byte)(len & 0xFF));
            stream.WriteByte((byte)((len >> 8) & 0xFF));
            stream.WriteByte((byte)type);
            stream.Write(payload, 0, len);
            stream.Flush();
        }

        static async Task ReadExactlyAsync(Stream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer, offset, buffer.Length - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
        }
    }
}
