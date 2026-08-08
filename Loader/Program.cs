using System;
using System.Collections.Generic;
using System.IO;
using System.Collections.Concurrent;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using SpaceEngineers;
using VRage.FileSystem;
using VRage.Library.Utils;

namespace LLELoader
{
	static class Logger
	{
		public const string LogPath = "LLELoader.log";
		private static StreamWriter _writer;
		private static readonly object _lock = new object();

		public static void Write(string msg)
		{
			try
			{
				lock (_lock)
				{
					if (_writer == null)
						_writer = new StreamWriter(LogPath, false);

					_writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + msg);
					_writer.Flush();
				}
			}
			catch { }
		}
	}

	class ChannelConfig
	{
		public string LlmUrl { get; set; } = "http://localhost:8080/v1/chat/completions";
		public string Model { get; set; } = "qwen";
		public string ApiKey { get; set; } = "";
		public string Provider { get; set; } = "local";
		public bool EnableThinking { get; set; } = false;
		// Characters, not tokens: the mod counts its conversation in characters and this is the
		// budget it counts against.
		public int ContextChars { get; set; } = 100000;
		public int MaxTokens { get; set; } = 20000;
	}

	class LoaderConfig
	{
		public ChannelConfig[] Channels { get; set; }
		public bool EnableProxy { get; set; } = false;
		public string ProxyUrl { get; set; } = "";
		public float ScreenshotScale { get; set; } = 0.5f;
		public float SimulationSpeed { get; set; } = 1.0f;
	}

	class Channel
	{
		public readonly int Id;
		public readonly ChannelConfig Config;

		// A queue per request, not one per channel: an abandoned stream keeps writing into its own,
		// so there is nothing to drain and no window where its chunks land in the next request's.
		private class Request
		{
			public readonly ConcurrentQueue<LLE.FromLLM> Chunks = new ConcurrentQueue<LLE.FromLLM>();
			public readonly CancellationTokenSource Cancel = new CancellationTokenSource();
		}

		private volatile Request _current;

		public Channel(int id, ChannelConfig config)
		{
			Id = id;
			Config = config;
		}

		public bool GetChunk(out LLE.FromLLM m)
		{
			var request = _current;
			if (request == null) { m = null; return false; }
			return request.Chunks.TryDequeue(out m);
		}

		public void Send(string requestJson)
		{
			Abandon();

			var request = new Request();
			_current = request;
			var _ = AskLlmStreaming(requestJson, request);
		}

		public void Cancel()
		{
			Abandon();
			Logger.Write($"[LLM:{Id}] cancelled");
		}

		private void Abandon()
		{
			var request = _current;
			if (request == null) return;

			_current = null;
			request.Cancel.Cancel();
			// Not disposed: the abandoned SendAsync still holds a registration on this token, and
			// disposing under it is the one thing the source must not do. The task ends on its own.
		}

		private static void Quoted(StringBuilder sb, string s)
		{
			sb.Append('"');
			foreach (var c in s)
			{	if (c == '"' || c == '\\') sb.Append('\\');
				sb.Append(c);
			}
			sb.Append('"');
		}

		private async Task AskLlmStreaming(string requestJson, Request request)
		{
			// Must stay before the first await: Abandon disposes the source, and only the token
			// survives that.
			var token = request.Cancel.Token;

			Action<LLE.MessageType, string> emit = (type, payload) =>
				request.Chunks.Enqueue(new LLE.FromLLM { Type = type, Payload = payload });

			try
			{
				var head = new StringBuilder("{\"model\":");
				Quoted(head, Config.Model);
				head.Append(",\"max_tokens\":").Append(Config.MaxTokens);
				head.Append(",\"stream\":true");

				if (Config.Provider == "local")
					head.Append(",\"chat_template_kwargs\":{\"enable_thinking\":")
						.Append(Config.EnableThinking ? "true" : "false").Append('}');
				else if (Config.Provider == "zai")
					head.Append(",\"thinking\":{\"type\":\"")
						.Append(Config.EnableThinking ? "enabled" : "disabled").Append("\"}");
				else //if (Config.Provider == "openrouter")
					head.Append(",\"reasoning\":{\"enabled\":")
						.Append(Config.EnableThinking ? "true" : "false").Append('}');

				head.Append(',').Append(requestJson).Append('}');

				var body = head.ToString();

				var httpRequest = new HttpRequestMessage(HttpMethod.Post, Config.LlmUrl)
				{	Content = new StringContent(body, Encoding.UTF8, "application/json")
				};
				if (!string.IsNullOrEmpty(Config.ApiKey))
					httpRequest.Headers.Add("Authorization", "Bearer " + Config.ApiKey);
				var response = await MessageBroker.Http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

				if (!response.IsSuccessStatusCode)
				{
					string errBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
					Logger.Write($"[LLM:{Id}] HTTP {(int)response.StatusCode} {response.StatusCode}: {errBody}");
					emit(LLE.MessageType.Error, $"HTTP {(int)response.StatusCode} {response.StatusCode}: {errBody}");
					return;
				}

				var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

				string line;
				int chunks = 0;

				using var reader = new StreamReader(stream);
				while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
				{
					if (token.IsCancellationRequested) break;

					if (!line.StartsWith("data:")) continue;
					var data = line.Substring(5).Trim();
					if (data == "[DONE]") break;

					chunks++;
					emit(LLE.MessageType.Chunk, data);
				}

				Logger.Write($"[LLM:{Id}] stream done: chunks={chunks}");

				emit(LLE.MessageType.Stop, null);
			}
			catch (Exception ex)
			{
				if (token.IsCancellationRequested) return;

				Logger.Write($"[LLM:{Id}] streaming error: " + ex.Message);
				emit(LLE.MessageType.Error, ex.Message);
			}
		}
	}

	static class MessageBroker
	{
		private static readonly LoaderConfig _config = LoadConfig();

		private static LoaderConfig LoadConfig()
		{
			try
			{
				string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
				string configPath = Path.Combine(exeDir, "LLELoader.json");
				if (File.Exists(configPath))
				{
					var text = File.ReadAllText(configPath);
					var c = System.Text.Json.JsonSerializer.Deserialize<LoaderConfig>(text);
					if (c != null)
					{
						Logger.Write($"[Config] Loaded {configPath}: proxy={c.EnableProxy}/{c.ProxyUrl} screenshotScale={c.ScreenshotScale} simulationSpeed={c.SimulationSpeed}");
						return c;
					}
				}
				Logger.Write("[Config] LLELoader.json not found, using defaults (local)");
			}
			catch (Exception ex)
			{
				Logger.Write("[Config] Failed to load config: " + ex.Message);
			}
			return new LoaderConfig();
		}

		private static readonly Channel[] _channels = BuildChannels();

		private static Channel[] BuildChannels()
		{
			var configs = _config.Channels;
			if (configs == null || configs.Length == 0)
			{
				Logger.Write("[Config] ERROR: no Channels in config, falling back to one default channel");
				configs = new[] { new ChannelConfig() };
			}

			var channels = new Channel[configs.Length];
			for (int i = 0; i < configs.Length; i++)
			{
				channels[i] = new Channel(i, configs[i]);
				var c = configs[i];
				Logger.Write($"[Config] channel {i}: url={c.LlmUrl} model={c.Model} provider={c.Provider}"
					+ $" thinking={c.EnableThinking} contextChars={c.ContextChars} maxTokens={c.MaxTokens}");
			}
			return channels;
		}

		private static Channel Get(int channel)
		{
			if (channel < 0 || channel >= _channels.Length) return null;
			return _channels[channel];
		}

		public static readonly HttpClient Http = new HttpClient(new HttpClientHandler
		{
			Proxy = (_config.EnableProxy && !string.IsNullOrEmpty(_config.ProxyUrl))
				? new WebProxy(_config.ProxyUrl) : null
		}) { Timeout = TimeSpan.FromSeconds(300) };

		public static bool GetChunkFromLLM(int channel, out LLE.FromLLM cmd)
		{
			var c = Get(channel);
			if (c == null) { cmd = null; return false; }
			return c.GetChunk(out cmd);
		}

		public static void SendMessageToLLM(int channel, string requestJson)
		{
			var c = Get(channel);
			if (c == null)
			{	Logger.Write($"[LLM] send to unknown channel {channel}, dropped");
				return;
			}

			c.Send(requestJson);
		}

		public static void CancelLLM(int channel)
		{
			var c = Get(channel);
			if (c != null) c.Cancel();
		}

		public static int GetContextChars(int channel)
		{
			var c = Get(channel);
			return c == null ? 0 : c.Config.ContextChars;
		}

		#region Screenshot

		private static string _screenshotPath;
		private static bool _screenshotHooked;
		private static bool _screenshotFinished = true;
		private static bool _screenshotSuccess;
		private static string _pendingScreenshot; // base64 PNG

		public static void RequestScreenshot()
		{
			_screenshotFinished = false;
			_screenshotSuccess = false;
			_pendingScreenshot = null;

			try
			{
				var directory = Path.Combine(MyFileSystem.UserDataPath, "Screenshots");
				Directory.CreateDirectory(directory);
				_screenshotPath = Path.Combine(directory, "LLE.png");

				File.Delete(_screenshotPath);

				if (!_screenshotHooked)
				{
					Sandbox.MySandboxGame.Static.OnScreenshotTaken += OnScreenshotTaken;
					_screenshotHooked = true;
				}

				VRageRender.MyRenderProxy.TakeScreenshot(
					new VRageMath.Vector2(_config.ScreenshotScale), _screenshotPath, false, true, false);
			}
			catch (Exception ex)
			{
				Logger.Write("[Screenshot] request failed: " + ex);
				_screenshotFinished = true;
			}
		}

		private static void OnScreenshotTaken(object sender, EventArgs e)
		{
			if (_screenshotFinished) return; // somebody else's screenshot, F2 for instance

			try
			{
				if (!File.Exists(_screenshotPath)) return;

				var bytes = File.ReadAllBytes(_screenshotPath);
				_pendingScreenshot = Convert.ToBase64String(bytes);
				_screenshotSuccess = true;
				Logger.Write($"[Screenshot] {_screenshotPath}, {bytes.Length} bytes");
			}
			catch (Exception ex)
			{
				Logger.Write("[Screenshot] read failed: " + ex);
			}
			finally
			{	_screenshotFinished = true;
			}
		}

		public static string TakeScreenshot()
		{
			var frame = _pendingScreenshot;
			_pendingScreenshot = null;
			return frame;
		}

		public static bool ScreenshotDone(out bool success)
		{
			success = _screenshotSuccess;
			return _screenshotFinished;
		}

		#endregion

		[HarmonyPatchCategory("Early")]
		static class Patch_SetupPaths
		{
			[HarmonyPatch(typeof(MyProgram), nameof(MyProgram.Main))]
			[HarmonyPrefix]
			static bool Prefix(string[] args)
			{
				Logger.Write("[LLELoader] MyProgram.Main prefix called.");
				string bin64 = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
				string seRoot = Directory.GetParent(bin64).FullName;

				MyFileSystem.ExePath = bin64;
				MyFileSystem.RootPath = seRoot;
				Environment.CurrentDirectory = bin64;

				Logger.Write("[LLELoader] Paths set: " + bin64);
				return true;
			}
		}

		[HarmonyPatchCategory("Early")]
		static class Patch_SimulationSpeed
		{
			[HarmonyPatch(typeof(Sandbox.Engine.Platform.FixedLoop), MethodType.Constructor,
				new[] { typeof(VRage.Stats.MyStats), typeof(string) })]
			[HarmonyPostfix]
			static void Postfix(Sandbox.Engine.Platform.FixedLoop __instance)
			{
				if (_config.SimulationSpeed == 1.0f) return;

				var waiter = AccessTools.FieldRefAccess<Sandbox.Engine.Platform.FixedLoop, WaitForTargetFrameRate>("m_waiter")(__instance);
				ref float frequency = ref AccessTools.FieldRefAccess<WaitForTargetFrameRate, float>("m_targetFrequency")(waiter);
				frequency *= _config.SimulationSpeed;
				Logger.Write($"[SimulationSpeed] update loop paced at {frequency} Hz");
			}
		}

		[HarmonyPatchCategory("Early")]
		static class Patch_ExperimentalText
		{
			[HarmonyPatch(typeof(VRage.MyTexts), nameof(VRage.MyTexts.LoadTexts))]
			[HarmonyPostfix]
			static void Postfix()
			{
				try
				{
					var package = AccessTools.Field(typeof(VRage.MyTexts), "m_package")
						.GetValue(null) as VRage.MyLocalizationPackage;
					if (package == null)
					{
						Logger.Write("[LLELoader] MyTexts.m_package not found");
						return;
					}
					package.AddMessage("PerformanceWarningHeading_ExperimentalMode", "VERY EXPERIMENTAL MODE", true);
					Logger.Write("[LLELoader] Experimental mode plate text replaced");
				}
				catch (Exception ex)
				{
					Logger.Write("[LLELoader] Experimental text patch failed: " + ex);
				}
			}
		}

		[HarmonyPatchCategory("Late")]
		static class Patch_ScriptManagerLoadData
		{
			private static readonly string[] BridgeMethods =
				[ "IsPresent", "GetChunkFromLLM", "SendMessageToLLM", "CancelLLM", "SetSystemPrompt",
				  "GetContextChars", "RequestScreenshot", "ScreenshotDone", "TakeScreenshot" ];
			private static readonly HashSet<MethodInfo> _patchedMethods = new HashSet<MethodInfo>();

			[HarmonyPatch("Sandbox.Game.World.MyScriptManager, Sandbox.Game", "LoadData")]
			[HarmonyPostfix]
			static void Postfix()
			{
				Logger.Write("[LLELoader] ScriptManager.LoadData postfix started");

				try
				{
					var assemblies = AppDomain.CurrentDomain.GetAssemblies();

					bool foundBridge = false;
					foreach (var a in assemblies)
					{
						var type = a.GetType("LLE.LLE_Loader", false);
						if (type != null)
						{
							Logger.Write("[LLELoader] Found LLE.LLE_Loader in assembly: " + a.GetName().Name);
							foundBridge = true;

							ApplyPatch(type);
						}
					}

					if (!foundBridge)
						Logger.Write("[LLELoader] ERROR: LLE.LLE_Loader type not found in any loaded assembly!");
				}
				catch (Exception ex)
				{
					Logger.Write("[LLELoader] Patching failed with exception: " + ex.ToString());
				}
			}

			private static void ApplyPatch(Type type)
			{
				for (int i = 0; i < BridgeMethods.Length; i++)
				{
					string methodName = BridgeMethods[i];
					MethodInfo original = AccessTools.Method(type, methodName);
					if (original == null) continue;
					if (_patchedMethods.Contains(original)) continue;

					var smld = typeof(Patch_ScriptManagerLoadData);
					HarmonyMethod prefix;
					switch (methodName)
					{
						case "IsPresent": prefix = new HarmonyMethod(smld, nameof(Prefix_IsPresent)); break;
						case "GetChunkFromLLM": prefix = new HarmonyMethod(smld, nameof(Prefix_GetChunkFromLLM)); break;
						case "SendMessageToLLM": prefix = new HarmonyMethod(smld, nameof(Prefix_SendMessageToLLM)); break;
						case "CancelLLM": prefix = new HarmonyMethod(smld, nameof(Prefix_CancelLLM)); break;
						case "GetContextChars": prefix = new HarmonyMethod(smld, nameof(Prefix_GetContextChars)); break;
						case "RequestScreenshot": prefix = new HarmonyMethod(smld, nameof(Prefix_RequestScreenshot)); break;
						case "ScreenshotDone": prefix = new HarmonyMethod(smld, nameof(Prefix_ScreenshotDone)); break;
						case "TakeScreenshot": prefix = new HarmonyMethod(smld, nameof(Prefix_TakeScreenshot)); break;
						default: continue;
					}

					new Harmony("lle.loader.bridge." + methodName).Patch(
						original: original,
						prefix: prefix
					);

					_patchedMethods.Add(original);
					Logger.Write("[LLELoader] Patched LLE.LLE_Loader." + methodName);
				}
			}

			static bool Prefix_IsPresent(ref bool __result)
			{
				__result = true;
				return false;
			}

			static bool Prefix_GetChunkFromLLM(int channel, out LLE.FromLLM m, ref bool __result)
			{
				__result = GetChunkFromLLM(channel, out m);
				return false;
			}

			static bool Prefix_SendMessageToLLM(int channel, string requestJson)
			{
				SendMessageToLLM(channel, requestJson);
				return false;
			}

			static bool Prefix_CancelLLM(int channel)
			{
				CancelLLM(channel);
				return false;
			}

			static bool Prefix_GetContextChars(int channel, ref int __result)
			{
				__result = GetContextChars(channel);
				return false;
			}

			static bool Prefix_RequestScreenshot()
			{
				RequestScreenshot();
				return false;
			}

			static bool Prefix_ScreenshotDone(out bool success, ref bool __result)
			{
				__result = ScreenshotDone(out success);
				return false;
			}

			static bool Prefix_TakeScreenshot(ref string __result)
			{
				__result = TakeScreenshot();
				return false;
			}

		}  // Patch_ScriptManagerLoadData class ends here

		static class Program
		{
			[STAThread]
			static void Main(string[] args)
			{
				Logger.Write("[LLELoader] Starting... applying early patches.");
				Logger.Write("Log file location: " + Path.GetFullPath(Logger.LogPath));

				new Harmony("lle.loader.early").PatchCategory("Early");
				new Harmony("lle.loader.late").PatchCategory("Late");

				try
				{
					Logger.Write("[LLELoader] Calling MyProgram.Main...");
					string[] gameArgs = new string[args.Length + 1];
					Array.Copy(args, gameArgs, args.Length);
					gameArgs[args.Length] = "-skipintro";
					MyProgram.Main(gameArgs);
					Logger.Write("[LLELoader] MyProgram.Main returned.");
				}

				catch (Exception ex) { Logger.Write("[LLELoader] MyProgram.Main threw: " + ex.ToString()); }
			}
		}
	}
}
