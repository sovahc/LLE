using System;
using System.Collections.Generic;
using System.IO;
using System.Collections.Concurrent;
using System.Reflection;
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

		private static void Init()
		{
			if (_writer == null)
				_writer = new StreamWriter(LogPath, false);
		}

		public static void Write(string msg)
		{
			Init();
			try
			{
				_writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + msg);
				_writer.Flush();
			}
			catch { }
		}
	}

	class LlmConfig
	{
		public string LlmUrl { get; set; } = "http://localhost:8080/v1/chat/completions";
		public string Model { get; set; } = "qwen";
		public string ApiKey { get; set; } = "";
		public string Provider { get; set; } = "local";
		public bool EnableThinking { get; set; } = false;
		public int ContextWindow { get; set; } = 100000;
		public int MaxTokens { get; set; } = 20000;
	}

	static class MessageBroker
	{
		private static string _systemPrompt = "";

		private static readonly LlmConfig _config = LoadConfig();

		private static LlmConfig LoadConfig()
		{
			try
			{
				string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
				string configPath = Path.Combine(exeDir, "LLELoader.json");
				if (File.Exists(configPath))
				{
					var text = File.ReadAllText(configPath);
					var c = System.Text.Json.JsonSerializer.Deserialize<LlmConfig>(text);
					if (c != null)
					{
						Logger.Write($"[Config] Loaded {configPath}: url={c.LlmUrl} model={c.Model} provider={c.Provider} thinking={c.EnableThinking} contextWindow={c.ContextWindow} maxTokens={c.MaxTokens}");
						return c;
					}
				}
				Logger.Write("[Config] lle_loader.json not found, using defaults (local)");
			}
			catch (Exception ex)
			{
				Logger.Write("[Config] Failed to load config: " + ex.Message);
			}
			return new LlmConfig();
		}

		private static readonly Queue<string> _chatContext = new Queue<string>();

		private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
		
		private static readonly ConcurrentQueue<LLE.FromLLM> _commandQueue = new ConcurrentQueue<LLE.FromLLM>();

		public static bool GetChunkFromLLM(out LLE.FromLLM cmd)
		{
			return _commandQueue.TryDequeue(out cmd);
		}

		public static void SendMessageToLLM(string text)
		{
			_chatContext.Enqueue(text);

			var context = "\n" + string.Join("\n", _chatContext);
			var _ = AskLlmStreaming(context);
		}

		public static void SetSystemPrompt(string text)
		{
			Logger.Write("[SetSystemPrompt] length=" + text.Length);

			_systemPrompt = text;
		}

		private static async Task AskLlmStreaming(string chatContext)
		{
			try
			{
				// Thinking: without the channel Gemma matches patterns, with it she checks her own
				// trace — 70% against 98% on placement. Measured in the GemmaBuilder project.
				// The budget covers the reasoning too, which runs to ~10k tokens on a multi-block job.
				var payload = new Dictionary<string, object>
				{
					["model"] = _config.Model,
					["messages"] = new[]
					{
						new { role = "system", content = _systemPrompt },
						new { role = "user",   content = chatContext }
					},
					["max_tokens"] = _config.MaxTokens,
					["stream"] = true,
				};
				if (_config.Provider == "local")
					payload["chat_template_kwargs"] = new { enable_thinking = _config.EnableThinking };
				else //if (_config.Provider == "openrouter")
					payload["reasoning"] = new { enabled = _config.EnableThinking };

				var body = System.Text.Json.JsonSerializer.Serialize(payload);

				var request = new HttpRequestMessage(HttpMethod.Post, _config.LlmUrl)
				{	Content = new StringContent(body, Encoding.UTF8, "application/json")
				};
				if (!string.IsNullOrEmpty(_config.ApiKey))
					request.Headers.Add("Authorization", "Bearer " + _config.ApiKey);
				var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

				if (!response.IsSuccessStatusCode)
				{
					string errBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
					Logger.Write($"[LLM] HTTP {(int)response.StatusCode} {response.StatusCode}: {errBody}");
					_commandQueue.Enqueue(new LLE.FromLLM { Type = LLE.MessageType.Error, Payload = $"HTTP {(int)response.StatusCode} {response.StatusCode}: {errBody}" });
					return;
				}

				var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

				string line;

				using var reader = new StreamReader(stream);
				while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
				{
					if (!line.StartsWith("data:")) continue;
					var data = line.Substring(5).Trim();
					if (data == "[DONE]") break;

					using var doc = System.Text.Json.JsonDocument.Parse(data);
					var root = doc.RootElement;
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
							_commandQueue.Enqueue(new LLE.FromLLM
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
								_commandQueue.Enqueue(new LLE.FromLLM
								{
									Type = LLE.MessageType.Content,
									Payload = content
								});
							}
						}
					}
				}

				_commandQueue.Enqueue(new LLE.FromLLM { Type = LLE.MessageType.Stop, Payload = null });
			}
			catch (Exception ex)
			{
				Logger.Write("[LLM] streaming error: " + ex.Message);
				_commandQueue.Enqueue(new LLE.FromLLM { Type = LLE.MessageType.Error, Payload = ex.Message });
			}
		}

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
				[ "IsPresent", "GetChunkFromLLM", "SendMessageToLLM", "SetSystemPrompt", "GetContextStatus", "RestartContext" ];
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
						case "GetContextStatus": prefix = new HarmonyMethod(smld, nameof(Prefix_GetContextStatus)); break;
						case "RestartContext": prefix = new HarmonyMethod(smld, nameof(Prefix_RestartContext)); break;
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

			static bool Prefix_GetChunkFromLLM(out LLE.FromLLM m, ref bool __result)
			{
				__result = GetChunkFromLLM(out m);
				return false;
			}

			static bool Prefix_SendMessageToLLM(string text)
			{
				SendMessageToLLM(text);
				return false;
			}

			static bool Prefix_SetSystemPrompt(string text)
			{
				SetSystemPrompt(text);
				return false;
			}

			static bool Prefix_GetContextStatus(out int usedChars, out int totalChars)
			{
				int chars = _systemPrompt.Length;
				foreach (var s in _chatContext) chars += s.Length;
				usedChars = chars;
				totalChars = _config.ContextWindow;
				return false;
			}

			static bool Prefix_RestartContext()
			{
				_chatContext.Clear();
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
