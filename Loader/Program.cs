using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics;
using HarmonyLib;
using Sandbox.Engine.Utils;
using SpaceEngineers;
using VRage.FileSystem;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace LLELoader
{
    // Real implementation that replaces the stub at runtime
    static class Logger
    {
        public const string LogPath = "lle_loader.log";
        private static StreamWriter _writer;

        private static void Init()
        {
            if (_writer == null)
            {
                _writer = new StreamWriter(LogPath, false);
            }
        }

        public static void Write(string msg)
        {
            Init();
            try 
            {
                _writer.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + msg);
                _writer.Flush();
            }
            catch {}
        }
    }

    // Real implementation that replaces the stub at runtime
    static class LoaderImpl
    {
        public static bool IsPresent() => true;
    }

    static class SocketImpl
    {
        private const string Host = "127.0.0.1";
        private const int Port = 8081;

        private static TcpClient _client;
        private static NetworkStream _stream;

        private static readonly Stopwatch _clock = Stopwatch.StartNew();
        private static double _nextReconnectTime;
        private static float _reconnectDelay = 0.5f;
        private const float MaxReconnectDelay = 10f;

        private static double Now => _clock.Elapsed.TotalSeconds;

        public static void Update()
        {
            if (!IsConnected() && Now >= _nextReconnectTime)
            {
                if (TryConnect()) ResetBackoff();
                else IncreaseBackoff();
            }
        }

        private static bool TryConnect()
        {
            try
            {
                CloseInternal();
                _client = new TcpClient(Host, Port);
                _client.NoDelay = true;
                _stream = _client.GetStream();
                Logger.Write("[SocketImpl] Connected");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Write("[SocketImpl] Connect failed: " + ex.Message);
                return false;
            }
        }

        private static void CloseInternal()
        {
            try { _stream?.Close(); _client?.Close(); } catch {}
            _stream = null; _client = null;
        }

        public static bool Send(byte[] data, int length)
        {
            try
            {
                if (_stream == null) return false;
                _stream.Write(data, 0, length);
                return true;
            }
            catch { Disconnect(); return false; }
        }

        public static int Receive(byte[] buffer, int offset, int maxLength)
        {
            try
            {
                if (_stream == null) return -1;
                if (!_stream.DataAvailable) return 0;
                int read = _stream.Read(buffer, offset, maxLength);
                return read >= 0 ? read : -1;
            }
            catch { Disconnect(); return -1; }
        }

        public static bool IsConnected() => _client != null && _client.Connected;

        public static void Disconnect()
        {   Logger.Write("[SocketImpl] Disconnect");
            CloseInternal();
            IncreaseBackoff();
        }

        private static void IncreaseBackoff()
        {
            _nextReconnectTime = Now + _reconnectDelay;
            _reconnectDelay = Math.Min(_reconnectDelay * 2, MaxReconnectDelay);
        }

        private static void ResetBackoff() => _reconnectDelay = 0.5f;
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
            return true; // continue into original Main
        }
    }

    [HarmonyPatchCategory("Late")]
    static class Patch_ScriptManagerLoadData
    {
        private static readonly string[] BridgeMethods = { "IsPresent", "Update", "Send", "Receive", "IsConnected", "Disconnect" };
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
                            if (_patchedMethods.Contains(original)) continue; // Already patched in this assembly

                            HarmonyMethod prefix;
                            switch (methodName)
                            {
                                case "IsPresent":    prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_IsPresent)); break;
                                case "Update":       prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_Update)); break;
                                case "Send":         prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_Send)); break;
                                case "Receive":      prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_Receive)); break;
                                case "IsConnected":  prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_IsConnected)); break;
                                case "Disconnect":   prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_Disconnect)); break;
                                default:             continue;
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

        // Prefix patches — intercept calls before stubs execute, set __result, return false to skip original
        static bool Prefix_Update()
        {
            SocketImpl.Update();
            return false;
        }

        static bool Prefix_IsPresent(ref bool __result)
        {
            __result = LoaderImpl.IsPresent();
            return false;
        }

        static bool Prefix_Send(byte[] data, int length, ref bool __result)
        {
            __result = SocketImpl.Send(data, length);
            return false;
        }

        static bool Prefix_Receive(byte[] buffer, int offset, int maxLength, ref int __result)
        {
            __result = SocketImpl.Receive(buffer, offset, maxLength);
            return false;
        }

        static bool Prefix_IsConnected(ref bool __result)
        {
            __result = SocketImpl.IsConnected();
            return false;
        }

        static bool Prefix_Disconnect()
        {
            SocketImpl.Disconnect();
            return false;
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Logger.Write("[LLELoader] Starting... applying early patches.");
            Logger.Write("Log file location: " + Path.GetFullPath(Logger.LogPath));
            
            // Apply early patches (path setup before game starts)
            new Harmony("lle.loader.early").PatchCategory("Early");
            new Harmony("lle.loader.late").PatchCategory("Late");

            try
            {
                // Start Space Engineers — late patches fire during script loading
                Logger.Write("[LLELoader] Calling MyProgram.Main...");
                string[] gameArgs = new string[args.Length + 1];
                Array.Copy(args, gameArgs, args.Length);
                gameArgs[args.Length] = "-skipintro";
                MyProgram.Main(gameArgs);
                Logger.Write("[LLELoader] MyProgram.Main returned.");
            }
            catch (Exception ex)
            {
                Logger.Write("[LLELoader] MyProgram.Main threw: " + ex.ToString());
            }
        }
    }
}
