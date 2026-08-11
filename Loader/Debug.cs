using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

using Sandbox;
using Sandbox.Game.World;

namespace LLELoader
{
	// One line of JSON in, one line of JSON out. Silent unless DebugPort is set in the config.
	static class Debug
	{
		private const int MaxChannels = 16;

		private static bool _hold;
		private static readonly string[] _request = new string[MaxChannels];
		private static readonly bool[] _held = new bool[MaxChannels];

		private static volatile bool _running;

		private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _chat =
			new System.Collections.Concurrent.ConcurrentQueue<string>();

		private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _events =
			new System.Collections.Concurrent.ConcurrentQueue<string>();

		public static void Start(int port)
		{
			if (port <= 0 || _running) return;

			_running = true;
			var thread = new Thread(() => Listen(port)) { IsBackground = true, Name = "LLE.Debug" };
			thread.Start();
		}

		private static void Listen(int port)
		{
			TcpListener listener;
			try
			{
				listener = new TcpListener(IPAddress.Loopback, port);
				listener.Start();
				Logger.Write($"[Debug] listening on 127.0.0.1:{port}");
			}
			catch (Exception ex)
			{
				Logger.Write("[Debug] listen failed: " + ex.Message);
				_running = false;
				return;
			}

			while (true)
			{
				try
				{
					var client = listener.AcceptTcpClient();
					var thread = new Thread(() => Serve(client)) { IsBackground = true, Name = "LLE.Debug.Client" };
					thread.Start();
				}
				catch (Exception ex)
				{
					Logger.Write("[Debug] accept failed: " + ex.Message);
					Thread.Sleep(1000);
				}
			}
		}

		private static void Serve(TcpClient client)
		{
			try
			{
				using (client)
				using (var stream = client.GetStream())
				using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8))
				using (var writer = new System.IO.StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
				{
					string line;
					while ((line = reader.ReadLine()) != null)
					{
						if (line.Length == 0) continue;

						string answer;
						try { answer = Handle(line); }
						catch (Exception ex) { answer = Failed(ex.Message); }

						writer.WriteLine(answer);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Write("[Debug] client dropped: " + ex.Message);
			}
		}

		private static string Handle(string line)
		{
			using (var document = JsonDocument.Parse(line))
			{
				var root = document.RootElement;

				JsonElement field;
				if (!root.TryGetProperty("cmd", out field) || field.ValueKind != JsonValueKind.String)
					return Failed("no cmd");

				var cmd = field.GetString();
				if (cmd != "status") Logger.Write("[Debug] " + line);

				switch (cmd)
				{
					case "status":     return Status();
					case "chat":       return Chat(root);
					case "wait":       return Wait(root);
					case "mode":       return Mode(root);
					case "request":    return Request(root);
					case "call":       return Call(root);
					case "release":    return Release(root);
					case "load_last":  return LoadLast();
					case "screenshot": return Screenshot();
					case "quit":       return Quit();
				}

				return Failed("unknown cmd " + cmd);
			}
		}

		#region commands

		private static string Status()
		{
			var sb = new StringBuilder("{\"ok\":true,\"state\":");
			Quoted(sb, State());
			sb.Append(",\"mode\":").Append(_hold ? "\"hold\"" : "\"pass\"");

			sb.Append(",\"held\":[");
			bool first = true;
			lock (_request)
				for (int i = 0; i < MaxChannels; ++i)
				{
					if (!_held[i]) continue;
					if (!first) sb.Append(',');
					first = false;
					sb.Append(i);
				}
			sb.Append("]}");

			return sb.ToString();
		}

		// Reaches the bot even while it is paused: the mod picks this up on its own update.
		private static string Chat(JsonElement root)
		{
			JsonElement text;
			if (!root.TryGetProperty("text", out text) || text.ValueKind != JsonValueKind.String)
				return Failed("chat needs text");

			// Whatever the bot said before this task is not an answer to it.
			string stale;
			while (_events.TryDequeue(out stale)) { }

			_chat.Enqueue(text.GetString());
			return "{\"ok\":true}";
		}

		public static bool TakeChat(out string message)
		{
			return _chat.TryDequeue(out message);
		}

		// Blocks until the bot says something or goes idle, so nothing has to poll the game log.
		private static string Wait(JsonElement root)
		{
			JsonElement value;
			int seconds = root.TryGetProperty("timeout", out value) && value.ValueKind == JsonValueKind.Number
				? value.GetInt32() : 60;

			var deadline = DateTime.UtcNow.AddSeconds(seconds);
			while (true)
			{
				string kind;
				if (_events.TryDequeue(out kind))
					return "{\"ok\":true,\"event\":" + kind + "}";

				if (DateTime.UtcNow >= deadline) return "{\"ok\":true,\"event\":{\"kind\":\"timeout\"}}";
				Thread.Sleep(50);
			}
		}

		// Called from the mod: what the bot said, and when it went idle.
		public static void PushEvent(string kind, string text)
		{
			if (!_running) return;

			while (_events.Count > 200)
			{	string dropped;
				_events.TryDequeue(out dropped);
			}

			var sb = new StringBuilder("{\"kind\":");
			Quoted(sb, kind ?? "");
			sb.Append(",\"text\":");
			Quoted(sb, text ?? "");
			_events.Enqueue(sb.Append('}').ToString());
		}

		private static string State()
		{
			if (MySandboxGame.Static == null) return "starting";

			var session = MySession.Static;
			if (session == null) return "menu";
			if (session.IsUnloading) return "unloading";
			return session.Ready ? "ingame" : "loading";
		}

		private static string Mode(JsonElement root)
		{
			JsonElement value;
			if (!root.TryGetProperty("value", out value) || value.ValueKind != JsonValueKind.String)
				return Failed("mode needs value");

			var text = value.GetString();
			if (text != "hold" && text != "pass") return Failed("mode is hold or pass");

			_hold = text == "hold";
			return "{\"ok\":true}";
		}

		// The exact request JSON the mod built. The game log does not keep it.
		private static string Request(JsonElement root)
		{
			int channel = Channel(root);
			if (channel < 0) return Failed("bad channel");

			string request;
			lock (_request) request = _request[channel];
			if (request == null) return Failed("no request seen on channel " + channel);

			var sb = new StringBuilder("{\"ok\":true,\"request\":");
			Quoted(sb, request);
			return sb.Append('}').ToString();
		}

		// Answers a held request the way the model would have: SSE chunks, then Stop.
		private static string Call(JsonElement root)
		{
			int channel = Channel(root);
			if (channel < 0) return Failed("bad channel");

			var chunk = BuildChunk(root);
			if (chunk == null) return Failed("call needs content or calls");

			var target = MessageBroker.Get(channel);
			if (target == null) return Failed("no channel " + channel);
			if (!target.Inject(chunk)) return Failed("channel " + channel + " is not waiting for an answer");

			lock (_request) _held[channel] = false;
			return "{\"ok\":true}";
		}

		private static string BuildChunk(JsonElement root)
		{
			var delta = new StringBuilder();

			JsonElement content;
			if (root.TryGetProperty("content", out content) && content.ValueKind == JsonValueKind.String)
			{
				delta.Append("\"content\":");
				Quoted(delta, content.GetString());
			}

			JsonElement calls;
			if (root.TryGetProperty("calls", out calls) && calls.ValueKind == JsonValueKind.Array)
			{
				if (delta.Length != 0) delta.Append(',');
				delta.Append("\"tool_calls\":[");

				int index = 0;
				foreach (var call in calls.EnumerateArray())
				{
					JsonElement name;
					if (!call.TryGetProperty("name", out name) || name.ValueKind != JsonValueKind.String)
						return null;

					JsonElement arguments;
					var text = call.TryGetProperty("arguments", out arguments)
						? (arguments.ValueKind == JsonValueKind.String ? arguments.GetString() : arguments.GetRawText())
						: "{}";

					if (index != 0) delta.Append(',');
					delta.Append("{\"index\":").Append(index).Append(",\"function\":{\"name\":");
					Quoted(delta, name.GetString());
					delta.Append(",\"arguments\":");
					Quoted(delta, text);
					delta.Append("}}");
					++index;
				}

				delta.Append(']');
			}

			if (delta.Length == 0) return null;

			return "{\"choices\":[{\"delta\":{" + delta + "}}]}";
		}

		// Lets a held request go to the real model after all.
		private static string Release(JsonElement root)
		{
			int channel = Channel(root);
			if (channel < 0) return Failed("bad channel");

			string request;
			lock (_request)
			{
				if (!_held[channel]) return Failed("channel " + channel + " is not held");
				request = _request[channel];
				_held[channel] = false;
			}

			var target = MessageBroker.Get(channel);
			if (target == null) return Failed("no channel " + channel);
			if (!target.Resume(request)) return Failed("channel " + channel + " has no pending request");

			return "{\"ok\":true}";
		}

		private static string LoadLast()
		{
			if (MySandboxGame.Static == null) return Failed("game is not up yet");
			if (MySession.Static != null) return Failed("a session is already loaded");

			MySandboxGame.Static.Invoke(() => MySessionLoader.LoadLastSession(), "LLE.Debug");
			return "{\"ok\":true}";
		}

		private static string Screenshot()
		{
			if (MySandboxGame.Static == null) return Failed("game is not up yet");

			MySandboxGame.Static.Invoke(() => MessageBroker.RequestScreenshot(), "LLE.Debug");
			return "{\"ok\":true}";
		}

		private static string Quit()
		{
			if (MySandboxGame.Static == null) return Failed("game is not up yet");

			MySandboxGame.Static.Invoke(() => MySandboxGame.ExitThreadSafe(), "LLE.Debug");
			return "{\"ok\":true}";
		}

		#endregion

		// Called on every request the mod sends. True keeps it here instead of the model.
		public static bool OnSend(int id, string requestJson)
		{
			if (!_running || id < 0 || id >= MaxChannels) return false;

			lock (_request)
			{
				_request[id] = requestJson;
				if (!_hold) return false;
				_held[id] = true;
			}

			Logger.Write($"[Debug] holding request on channel {id}");
			return true;
		}

		private static int Channel(JsonElement root)
		{
			JsonElement value;
			if (!root.TryGetProperty("channel", out value)) return 0;
			if (value.ValueKind != JsonValueKind.Number) return -1;

			int channel = value.GetInt32();
			return channel >= 0 && channel < MaxChannels ? channel : -1;
		}

		private static string Failed(string reason)
		{
			var sb = new StringBuilder("{\"ok\":false,\"error\":");
			Quoted(sb, reason);
			return sb.Append('}').ToString();
		}

		private static void Quoted(StringBuilder sb, string text)
		{
			sb.Append(JsonSerializer.Serialize(text));
		}
	}
}
