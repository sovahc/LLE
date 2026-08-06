using System;
using System.Collections.Generic;
using System.IO;
using System.Collections.Concurrent;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using SpaceEngineers;
using VRage.FileSystem;

namespace LLELoader
{
	static class Logger
	{
		public const string LogPath = "LLELoader.log";
		private static StreamWriter _writer;

		// One writer, and as many callers as there are channels streaming at once, plus the game
		// thread. StreamWriter is not thread-safe, so the lock is what keeps the log a log.
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
		public int ContextWindow { get; set; } = 100000;
		public int MaxTokens { get; set; } = 20000;
	}

	class LoaderConfig
	{
		// Index is the channel id the mod addresses. 0 is the one that executes commands.
		public ChannelConfig[] Channels { get; set; }
		public bool EnableProxy { get; set; } = false;
		public string ProxyUrl { get; set; } = "";
		// Fraction of the game window the screenshot is rendered at. The vision model rescales
		// anyway; this only decides how much detail survives to be rescaled.
		public float ScreenshotScale { get; set; } = 0.5f;
	}

	// One model behind one endpoint. The channel holds no conversation: the mod passes the whole
	// user message every time and decides what a turn is. Everything here is transport.
	class Channel
	{
		public readonly int Id;
		public readonly ChannelConfig Config;

		private string _systemPrompt = "";
		private string _stopString;

		private readonly ConcurrentQueue<LLE.FromLLM> _queue = new ConcurrentQueue<LLE.FromLLM>();

		public Channel(int id, ChannelConfig config)
		{
			Id = id;
			Config = config;
		}

		public void SetSystemPrompt(string text, string stop)
		{
			Logger.Write($"[SetSystemPrompt:{Id}] length={text.Length} stop={stop}");
			_systemPrompt = text;
			_stopString = stop;
		}

		public bool GetChunk(out LLE.FromLLM m)
		{
			return _queue.TryDequeue(out m);
		}

		// Fire and forget. Everything the request needs arrives as a string or is read before the
		// first await, i.e. still on the game thread — no collection owned by the game is ever
		// touched from the streaming task.
		public void Send(string userText, string imageBase64)
		{
			var _ = AskLlmStreaming(userText, imageBase64);
		}

		private async Task AskLlmStreaming(string chatContext, string screenshotBase64)
		{
			try
			{
				object userContent = chatContext;
				if (screenshotBase64 != null)
				{
					userContent = new object[]
					{
						new { type = "text", text = chatContext },
						new { type = "image_url", image_url = new { url = "data:image/png;base64," + screenshotBase64 } },
					};
				}

				// Thinking: without the channel Gemma matches patterns, with it she checks her own
				// trace — 70% against 98% on placement. Measured in the GemmaBuilder project.
				// The budget covers the reasoning too, which runs to ~10k tokens on a multi-block job.
				var payload = new Dictionary<string, object>
				{
					["model"] = Config.Model,
					["messages"] = new object[]
					{
						new { role = "system", content = (object)_systemPrompt },
						new { role = "user",   content = userContent }
					},
					["max_tokens"] = Config.MaxTokens,
					["stream"] = true,
				};
				if (Config.Provider == "local")
					payload["chat_template_kwargs"] = new { enable_thinking = Config.EnableThinking };
				else if (Config.Provider == "zai")
					payload["thinking"] = new { type = Config.EnableThinking ? "enabled" : "disabled" };
				else //if (Config.Provider == "openrouter")
					payload["reasoning"] = new { enabled = Config.EnableThinking };

				if (!string.IsNullOrEmpty(_stopString))
					payload["stop"] = _stopString;

				var body = System.Text.Json.JsonSerializer.Serialize(payload);

				var request = new HttpRequestMessage(HttpMethod.Post, Config.LlmUrl)
				{	Content = new StringContent(body, Encoding.UTF8, "application/json")
				};
				if (!string.IsNullOrEmpty(Config.ApiKey))
					request.Headers.Add("Authorization", "Bearer " + Config.ApiKey);
				var response = await MessageBroker.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

				if (!response.IsSuccessStatusCode)
				{
					string errBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
					Logger.Write($"[LLM:{Id}] HTTP {(int)response.StatusCode} {response.StatusCode}: {errBody}");
					_queue.Enqueue(new LLE.FromLLM { Type = LLE.MessageType.Error, Payload = $"HTTP {(int)response.StatusCode} {response.StatusCode}: {errBody}" });
					return;
				}

				var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

				string line;

				// finish_reason distinguishes a clean stop from a max_tokens cut (length) from a
				// stream that broke before the terminating chunk. The loop below ends the same way
				// for all three, so without finish_reason an empty response is ambiguous.
				string finishReason = null;
				bool sawFinishReason = false;
				int contentChunks = 0, reasoningChunks = 0;

				using var reader = new StreamReader(stream);
				while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
				{
					if (!line.StartsWith("data:")) continue;
					var data = line.Substring(5).Trim();
					if (data == "[DONE]") break;

					using var doc = System.Text.Json.JsonDocument.Parse(data);
					var root = doc.RootElement;
					// finish_reason is a sibling of delta under choices[0]; read it independently so it is
					// captured even on the terminating chunk where delta is absent or empty.
					if (root.TryGetProperty("choices", out var frChoices) && frChoices.GetArrayLength() > 0
						&& frChoices[0].TryGetProperty("finish_reason", out var fr)
						&& fr.ValueKind == System.Text.Json.JsonValueKind.String)
					{
						finishReason = fr.GetString();
						sawFinishReason = true;
					}

					if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
						&& choices[0].TryGetProperty("delta", out var delta))
					{
						// Reasoning — field name differs by provider:
						//   local (llama.cpp): "reasoning_content"
						//   openrouter:        "reasoning"
						string reasoning = null;
						if (delta.TryGetProperty("reasoning_content", out var reasoningProp))
							reasoning = reasoningProp.ValueKind == System.Text.Json.JsonValueKind.String ? reasoningProp.GetString() : null;
						else if (delta.TryGetProperty("reasoning", out var reasoningProp2))
							reasoning = reasoningProp2.ValueKind == System.Text.Json.JsonValueKind.String ? reasoningProp2.GetString() : null;

						if (!string.IsNullOrEmpty(reasoning))
						{
							reasoningChunks++;
							_queue.Enqueue(new LLE.FromLLM
							{
								Type = LLE.MessageType.Reasoning,
								Payload = reasoning
							});
						}
						if (delta.TryGetProperty("content", out var contentProp))
						{
							var content = contentProp.GetString();
							if (!string.IsNullOrEmpty(content))
							{
								contentChunks++;
								_queue.Enqueue(new LLE.FromLLM
								{
									Type = LLE.MessageType.Content,
									Payload = content
								});
							}
						}
					}
				}

				// finish_reason tells us why the response ended; a missing finish_reason means the
				// stream closed before the terminating chunk (proxy/network drop).
				Logger.Write($"[LLM:{Id}] stream done: "
					+ (sawFinishReason ? "finish_reason=" + (finishReason ?? "null") : "STREAM-ENDED-WITHOUT-FINISH-REASON")
					+ " contentChunks=" + contentChunks
					+ " reasoningChunks=" + reasoningChunks);

				_queue.Enqueue(new LLE.FromLLM { Type = LLE.MessageType.Stop, Payload = null });
			}
			catch (Exception ex)
			{
				Logger.Write($"[LLM:{Id}] streaming error: " + ex.Message);
				_queue.Enqueue(new LLE.FromLLM { Type = LLE.MessageType.Error, Payload = ex.Message });
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
						Logger.Write($"[Config] Loaded {configPath}: proxy={c.EnableProxy}/{c.ProxyUrl} screenshotScale={c.ScreenshotScale}");
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
					+ $" thinking={c.EnableThinking} contextWindow={c.ContextWindow} maxTokens={c.MaxTokens}");
			}
			return channels;
		}

		// The mod asks for channels by index; an index it invented is not a crash, it is silence.
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

		public static void SendMessageToLLM(int channel, string text)
		{
			var c = Get(channel);
			if (c == null)
			{	Logger.Write($"[LLM] send to unknown channel {channel}, dropped");
				return;
			}

			// The image rides with this one message only, and only on the channel that asked for
			// it. The mod's history keeps the text, so an old frame can never be mistaken for what
			// the bot is looking at now.
			string image = null;
			if (channel == 0)
			{	image = _pendingScreenshot;
				_pendingScreenshot = null;
			}

			c.Send(text, image);
		}

		public static int GetContextWindow(int channel)
		{
			var c = Get(channel);
			return c == null ? 0 : c.Config.ContextWindow;
		}

		public static void SetSystemPrompt(int channel, string text, string stop)
		{
			var c = Get(channel);
			if (c == null)
			{	Logger.Write($"[SetSystemPrompt] unknown channel {channel}, ignored");
				return;
			}
			c.SetSystemPrompt(text, stop);
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

				// OnScreenshotTaken carries neither a path nor a result, so a leftover file from
				// the previous shot would read as this one's success.
				File.Delete(_screenshotPath);

				if (!_screenshotHooked)
				{
					Sandbox.MySandboxGame.Static.OnScreenshotTaken += OnScreenshotTaken;
					_screenshotHooked = true;
				}

				// ignoreSprites: the frame is copied right after the game scene and before the
				// GUI, which keeps the HUD out and the mod's billboards in.
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

			_screenshotFinished = true;
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
		static class Patch_ExperimentalText
		{
			// The HUD plate draws MyTexts.GetString(PerformanceWarningHeading_ExperimentalMode)
			// every frame (Sandbox.Game MyGuiScreenHudBase.DrawString), so overwriting the entry
			// in the localization package is enough. LoadLanguage() calls MyTexts.Clear() before
			// every LoadTexts(), hence the postfix — it re-applies on a language switch too.
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
				[ "IsPresent", "GetChunkFromLLM", "SendMessageToLLM", "SetSystemPrompt", "GetContextWindow",
				  "RequestScreenshot", "ScreenshotDone" ];
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
						case "SetSystemPrompt": prefix = new HarmonyMethod(smld, nameof(Prefix_SetSystemPrompt)); break;
						case "GetContextWindow": prefix = new HarmonyMethod(smld, nameof(Prefix_GetContextWindow)); break;
						case "RequestScreenshot": prefix = new HarmonyMethod(smld, nameof(Prefix_RequestScreenshot)); break;
						case "ScreenshotDone": prefix = new HarmonyMethod(smld, nameof(Prefix_ScreenshotDone)); break;
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

			static bool Prefix_SendMessageToLLM(int channel, string text)
			{
				SendMessageToLLM(channel, text);
				return false;
			}

			static bool Prefix_SetSystemPrompt(int channel, string text, string stop)
			{
				SetSystemPrompt(channel, text, stop);
				return false;
			}

			static bool Prefix_GetContextWindow(int channel, ref int __result)
			{
				__result = GetContextWindow(channel);
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
