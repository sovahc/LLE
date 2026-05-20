using System;
using System.Collections.Generic;
using System.IO;
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

	// In-memory broker: replaces SocketImpl + Server
	static class MessageBroker
	{
		private static Dictionary<long, LLE.LastKnownState> _visionStates;

		private static readonly Queue<string> _chatContext = new Queue<string>();

		private static LLE.ServerCommand _pendingCommand;

		public static void SetVision(Dictionary<long, LLE.LastKnownState> states)
		{
			_visionStates = states;
		}

		public static void SetChat(string author, string text)
		{
			var entry = $"{author}: {text}";
			Logger.Write("[CHAT] " + entry);
			_chatContext.Enqueue(entry);
			if (_chatContext.Count > 50) _chatContext.Dequeue();

			if (text.Length > 0 && text[0] == '>')
			{
				_pendingCommand = new LLE.ServerCommand { CommandType = 0, Payload = text.Substring(1).Trim() };
			}
			else
			{
				var _ = RespondToChatAsync();
			}
		}

		public static bool GetCommand(out LLE.ServerCommand cmd)
		{
			cmd = _pendingCommand;
			_pendingCommand = null;
			return cmd != null;
		}

		private static async Task RespondToChatAsync()
		{
			try
			{
				string context = "\n" + string.Join("\n", _chatContext);
				string llmReply = await AskLlm(context);
				if (string.IsNullOrEmpty(llmReply)) return;
				Logger.Write("[LLM] " + llmReply);
				_pendingCommand = new LLE.ServerCommand { CommandType = 0, Payload = llmReply };
			}
			catch (Exception ex) { Logger.Write("[LLM] error: " + ex.Message); }
		}

		private static readonly HttpClient _http = new HttpClient();
		const string LlmUrl = "http://localhost:8080/v1/chat/completions";

		private static async Task<string> AskLlm(string chatContext)
		{
			var safeContext = System.Text.Json.JsonSerializer.Serialize(chatContext);

			var body = $"{{ \"model\": \"qwen\", \"messages\": [ {{ \"role\": \"system\", \"content\": \"Reply max 50 characters. No explanations.\" }}, {{ \"role\": \"user\", \"content\": {safeContext} }} ], \"max_tokens\": 64, \"stream\": false }}";

			var response = await _http.PostAsync(LlmUrl, new StringContent(body, Encoding.UTF8, "application/json"));
			var text = await response.Content.ReadAsStringAsync();

			try
			{
				using (var doc = System.Text.Json.JsonDocument.Parse(text))
				{
					var root = doc.RootElement;
					if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
						&& choices[0].TryGetProperty("message", out var message)
						&& message.TryGetProperty("content", out var content))
					{
						return content.GetString()?.Trim() ?? "";
					}
				}
			}
			catch (System.Text.Json.JsonException) { }

			return "";
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

	[HarmonyPatchCategory("Late")]
	static class Patch_ScriptManagerLoadData
	{
		private static readonly string[] BridgeMethods = { "IsPresent", "Update", "SetVision", "SetChat", "GetCommand", "DumpEntity" };
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
				foreach (var asm in assemblies)
				{
					var type = asm.GetType("LLE.LLE_Loader", false);
					if (type != null)
					{
						Logger.Write("[LLELoader] Found LLE.LLE_Loader in assembly: " + asm.GetName().Name);
						foundBridge = true;

						for (int i = 0; i < BridgeMethods.Length; i++)
						{
							string methodName = BridgeMethods[i];
							MethodInfo original = AccessTools.Method(type, methodName);
							if (original == null) continue;
							if (_patchedMethods.Contains(original)) continue;

							HarmonyMethod prefix;
							switch (methodName)
							{
								case "IsPresent": prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_IsPresent)); break;
								case "Update": prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_Update)); break;
								case "SetVision": prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_SetVision)); break;
								case "SetChat": prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_SetChat)); break;
								case "GetCommand": prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_GetCommand)); break;
								case "DumpEntity": prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_DumpEntity)); break;
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
				}

				if (!foundBridge)
					Logger.Write("[LLELoader] ERROR: LLE.LLE_Loader type not found in any loaded assembly!");
			}
			catch (Exception ex)
			{
				Logger.Write("[LLELoader] Patching failed with exception: " + ex.ToString());
			}
		}

		static bool Prefix_IsPresent(ref bool __result)
		{
			__result = LoaderImpl.IsPresent();
			return false;
		}

		static bool Prefix_Update()
		{
			return false;
		}

		static void Prefix_SetVision(Dictionary<long, LLE.LastKnownState> states)
		{
			MessageBroker.SetVision(states);
		}

		static void Prefix_SetChat(string author, string text)
		{
			MessageBroker.SetChat(author, text);
		}

		static bool Prefix_GetCommand(out LLE.ServerCommand cmd, ref bool __result)
		{
			__result = MessageBroker.GetCommand(out cmd);
			return false;
		}


		static void LogShape(object shape, string indent = "")
		{
			if (shape == null) return;
			var type = shape.GetType();
			Logger.Write(string.Format("{0}{1} ({2})", indent, type.Name, type.Namespace ?? ""));

			foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
				try
				{
					if (!prop.CanRead) continue;
					var indexers = prop.GetIndexParameters();
					object val;
					if (indexers.Length > 0)
						Logger.Write(string.Format("{0}  PROP [idx] {1}: {2}", indent, prop.Name, prop.PropertyType));
					else
					{
						val = prop.GetValue(shape);
						Logger.Write(string.Format("{0}  {1}: {2} ({3})", indent, prop.Name, val ?? "null", prop.PropertyType.Name));
					}
				}
				catch (Exception ex) { Logger.Write(string.Format("{0}  PROP ERROR {1}: {2}", indent, prop.Name, ex.Message)); }

			foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
				try
				{
					var val = field.GetValue(shape);
					Logger.Write(string.Format("{0}  FIELD {1}: {2} ({3})", indent, field.Name, val ?? "null", field.FieldType.Name));
				}
				catch (Exception ex) { Logger.Write(string.Format("{0}  FIELD ERROR {1}: {2}", indent, field.Name, ex.Message)); }
		}

		static void Prefix_DumpEntity(long entityId)
		{
			Logger.Write("=== DumpEntity: " + entityId);
			try
			{
				var entitiesType = AccessTools.TypeByName("Sandbox.Game.Entities.MyEntities");
				if (entitiesType == null) { Logger.Write("[Dump] MyEntities type not found."); return; }

				var getEntityMethod = AccessTools.Method(entitiesType, "GetEntityById", new[] { typeof(long), typeof(bool) });
				object entityInstance = getEntityMethod.Invoke(null, new object[] { entityId, true });
				if (entityInstance == null) { Logger.Write("[Dump] Entity is NULL."); return; }

				var model = AccessTools.Property(entityInstance.GetType(), "Model").GetValue(entityInstance);
				if (model == null) { Logger.Write("[Dump] Model is NULL."); return; }

				var shapes = AccessTools.Field(model.GetType(), "HavokCollisionShapes").GetValue(model);
				if (shapes == null) { Logger.Write("[Dump] HavokCollisionShapes is NULL."); return; }

				var arr = (Array)shapes;
				Logger.Write(string.Format("[Dump] Model: {0}, HavokShapes count={1}", model.GetType().Name, arr.Length));

				foreach (object shape in arr) LogShape(shape);
			}
			catch (Exception ex) { Logger.Write(string.Format("[Dump] Exception: {0}", ex.ToString())); }
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
} // namespace LLELoader ends here
