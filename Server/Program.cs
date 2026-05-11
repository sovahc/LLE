using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Collections.Generic;
using ProtoBuf;

namespace LLE.Server
{
	class Program
	{
		static void Main(string[] args)
		{
			var listener = new TcpListener(IPAddress.Loopback, 8080);
			listener.Start();
			Console.WriteLine("Listening on 127.0.0.1:8080");

			var chatBuffer = new Queue<string>();

			while (true)
			{
				var client = listener.AcceptTcpClient();
				var stream = client.GetStream();

				try
				{
					while (true)
					{
						// 1. Read 2 bytes length
						byte[] lenBuf = new byte[2];
						ReadExactly(stream, lenBuf);
						int length = lenBuf[0] | (lenBuf[1] << 8);

						// 2. Read 1 byte type
						byte[] typeBuf = new byte[1];
						ReadExactly(stream, typeBuf);
						int msgType = typeBuf[0];

						// 3. Read payload
						byte[] payload = new byte[length];
						ReadExactly(stream, payload);

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
								chatBuffer.Enqueue(chat.Text);
								if (chatBuffer.Count > 50) chatBuffer.Dequeue();

								// Simple echo bot for testing
								if (chat.Text.Contains("LLM")) {
									Console.WriteLine("LLM found in chat, replying...");
									SendFrame(stream, 2, new LLE.ServerCommand {
										CommandType = 0,
										Payload = "I am listening."
									});
								}
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
		}

		static void SendFrame<T>(NetworkStream stream, int type, T obj) {
			byte[] payload;
			using (var ms = new MemoryStream()) {
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

		static void ReadExactly(Stream stream, byte[] buffer)
		{
			int offset = 0;
			while (offset < buffer.Length)
			{
				int read = stream.Read(buffer, offset, buffer.Length - offset);
				if (read == 0) throw new EndOfStreamException();
				offset += read;
			}
		}
	}
}
