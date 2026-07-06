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
		public const string LogPath = "lle_loader.log";
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

	static class LoaderImpl
	{
		public static bool IsPresent() => true;
	}

	static class MessageBroker
	{
		const string LlmUrl = "http://localhost:8080/v1/chat/completions";
		private const int ContextWindow = 100000;
		private const int max_tokens = 1000;

		private static string _systemPrompt = "";

		private static readonly Queue<string> _chatContext = new Queue<string>();
		private static int _contextId;

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

		public static void SetHelp(string text)
		{
			Logger.Write("[SetHelp] " + text);

			_systemPrompt += "\n" + text;
		}

		private static async Task AskLlmStreaming(string chatContext)
		{
			try
			{
				int contextId = _contextId;
				var payload = new
				{
					model = "qwen",
					messages = new[]
					{
						new { role = "system", content = _systemPrompt },
						new { role = "user",   content = chatContext }
					},
					max_tokens = max_tokens,
					stream = true,
					chat_template_kwargs = new { enable_thinking = false }
				};
				var body = System.Text.Json.JsonSerializer.Serialize(payload);

				var request = new HttpRequestMessage(HttpMethod.Post, LlmUrl)
				{	Content = new StringContent(body, Encoding.UTF8, "application/json")
				};
				var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
				var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

				string line;

				using var reader = new StreamReader(stream);
				while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
				{
					if (_contextId != contextId) return;
						// Context was restarted while this request was in-flight
						// — discard stale chunks
					
					if (!line.StartsWith("data:")) continue;
					var data = line.Substring(5).Trim();
					if (data == "[DONE]") break;

					using var doc = System.Text.Json.JsonDocument.Parse(data);
					var root = doc.RootElement;
					if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
						&& choices[0].TryGetProperty("delta", out var delta))
					{
						if (delta.TryGetProperty("reasoning_content", out var reasoningProp))
						{
							var reasoning = reasoningProp.GetString();
							if (!string.IsNullOrEmpty(reasoning))
							{
								_commandQueue.Enqueue(new LLE.FromLLM
								{
									Type = LLE.MessageType.Reasoning,
									Payload = reasoning
								});
							}
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
			catch (Exception ex) { Logger.Write("[LLM] streaming error: " + ex.Message); }
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

		[HarmonyPatchCategory("Late")]
		static class Patch_ScriptManagerLoadData
		{
			private static readonly string[] BridgeMethods =
				[ "IsPresent", "GetChunkFromLLM", "SendMessageToLLM", "SetHelp", "GetContextStatus", "RestartContext" ];
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
						case "SetHelp": prefix = new HarmonyMethod(smld, nameof(Prefix_SetHelp)); break;
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
				__result = LoaderImpl.IsPresent();
				return false;
			}

			static bool Prefix_GetChunkFromLLM(out LLE.FromLLM m, ref bool __result)
			{
				__result = GetChunkFromLLM(out m);
				return false;
			}

			static void Prefix_SendMessageToLLM(string text)
			{
				SendMessageToLLM(text);
			}

			static void Prefix_SetHelp(string text)
			{
				SetHelp(text);
			}

			static bool Prefix_GetContextStatus(out int usedChars, out int totalChars)
			{
				int chars = _systemPrompt.Length;
				foreach (var s in _chatContext) chars += s.Length;
				usedChars = chars;
				totalChars = ContextWindow;
				return false;
			}

			static bool Prefix_RestartContext()
			{
				++_contextId;
				_chatContext.Clear();
				Logger.Write("[LLELoader] Context restarted: contextId=" + _contextId);
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
