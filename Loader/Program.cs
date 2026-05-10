using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Engine.Utils;
using SpaceEngineers;
using VRage.FileSystem;
using System.Net.Sockets;

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
        private const int Port = 8080;

        private static TcpClient _client;
        private static NetworkStream _stream;

        public static bool Connect()
        {
            try
            {
                if (_client != null && _client.Connected) return true;

                Disconnect();

                _client = new TcpClient(Host, Port);
                _client.NoDelay = true;
                _stream = _client.GetStream();

                Logger.Write("[SocketImpl] Connected to " + Host + ":" + Port);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Write("[SocketImpl] Connect failed: " + ex.Message);
                Disconnect();
                return false;
            }
        }

        public static void Disconnect()
        {
            try
            {
                if (_stream != null) { _stream.Close(); _stream = null; }
                if (_client != null) { _client.Close(); _client = null; }
            }
            catch (Exception ex)
            {
                Logger.Write("[SocketImpl] Disconnect error: " + ex.Message);
            }
        }

        public static bool Send(byte[] data, int length)
        {
            try
            {
                if (_stream == null) return false;
                _stream.Write(data, 0, length);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Write("[SocketImpl] Send failed: " + ex.Message);
                Disconnect();
                return false;
            }
        }

        public static int Receive(byte[] buffer, int maxLength)
        {
            try
            {
                if (_stream == null) return -1;

                // Probe mode — check connectivity without consuming data
                if (buffer == null || maxLength == 0)
                {
                    if (!_client.Connected)
                    {
                        Disconnect();
                        return -1;
                    }
                    return _stream.DataAvailable ? 1 : 0;
                }

                if (!_stream.DataAvailable) return 0;

                int read = _stream.Read(buffer, 0, maxLength);
                return read >= 0 ? read : -1;
            }
            catch (Exception ex)
            {
                Logger.Write("[SocketImpl] Receive failed: " + ex.Message);
                Disconnect();
                return -1;
            }
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
            return true; // continue into original Main
        }
    }

    [HarmonyPatchCategory("Late")]
    static class Patch_ScriptManagerLoadData
    {
        private static readonly string[] BridgeMethods = { "IsPresent", "Connect", "Disconnect", "Send", "Receive" };
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
                                case "Connect":      prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_Connect)); break;
                                case "Disconnect":   prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_Disconnect)); break;
                                case "Send":         prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_Send)); break;
                                case "Receive":      prefix = new HarmonyMethod(typeof(Patch_ScriptManagerLoadData), nameof(Prefix_Receive)); break;
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
        static bool Prefix_IsPresent(ref bool __result)
        {
            __result = LoaderImpl.IsPresent();
            return false;
        }

        static bool Prefix_Connect(ref bool __result)
        {
            __result = SocketImpl.Connect();
            return false;
        }

        static bool Prefix_Disconnect()
        {
            SocketImpl.Disconnect();
            return false;
        }

        static bool Prefix_Send(byte[] data, int length, ref bool __result)
        {
            __result = SocketImpl.Send(data, length);
            return false;
        }

        static bool Prefix_Receive(byte[] buffer, int maxLength, ref int __result)
        {
            __result = SocketImpl.Receive(buffer, maxLength);
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
