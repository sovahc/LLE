using System;
using System.Net;
using System.Net.Sockets;
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

			while (true)
			{
				var client = listener.AcceptTcpClient();
				var stream = client.GetStream();

				try
				{
					while (true)
					{
						byte[] header = new byte[4];
						ReadExactly(stream, header);

						int length = (header[0])
							| (header[1] << 8)
							| (header[2] << 16)
							| (header[3] << 24);

						byte[] payload = new byte[length];
						ReadExactly(stream, payload);

						var dto = Serializer.Deserialize<LLE.LastKnownState>(new System.IO.MemoryStream(payload));
						Console.WriteLine($"Position: X={dto.X:F2} Y={dto.Y:F2} Z={dto.Z:F2}");
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

		static void ReadExactly(System.IO.Stream stream, byte[] buffer)
		{
			int offset = 0;
			while (offset < buffer.Length)
			{
				int read = stream.Read(buffer, offset, buffer.Length - offset);
				if (read == 0) throw new System.IO.EndOfStreamException();
				offset += read;
			}
		}
	}
}
