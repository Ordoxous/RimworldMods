using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using RimBridgeServer.Sdk;
using Verse;

namespace Ordo.BridgeTools;

// Where to reach a game the player started themselves. RimBridgeServer's standalone mode writes its port and token only
// to the game's log, and every other RimWorld started later (a test game through GABS, headless too) replaces that log,
// so the token was lost. When the bridge runs standalone, its port and token are written to
// ~/.gabs/rimworld/standalone.json as well, with the game's process id; a GABS launch is left alone (GABS keeps its own).
// RimBridgeServer creates this class while it discovers the bridge tools, before its server exists, so the constructor
// waits for the server on a background thread.
public sealed class BridgeConnection
{
    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static int started;

    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gabs", "rimworld", "standalone.json");

    public BridgeConnection()
    {
        if (Interlocked.Exchange(ref started, 1) == 0)
        {
            ErrorMirror.Start();
            try
            {
                HistoryTools.Start();
            }
            catch (Exception e)
            {
                Log.Warning($"[BridgeTools] Couldn't start the job and origin history: {e.Message}");
            }
            new Thread(WaitAndWrite) { IsBackground = true, Name = "Ordo bridge connection" }.Start();
        }
    }

    [Tool("ordo/bridge_connection", Description = "This game's bridge port and token, whether it runs standalone or under GABS, and the file a standalone game writes them to")]
    public object Connection()
    {
        var (port, token) = Server();
        return new { port, token, standalone = !UnderGabs(), file = FilePath, pid = Environment.ProcessId };
    }

    private static void WaitAndWrite()
    {
        try
        {
            // Two minutes at most: the bridge starts once play data has loaded
            for (var i = 0; i < 240; i++)
            {
                var (port, token) = Server();
                if (port > 0 && !string.IsNullOrEmpty(token))
                {
                    if (!UnderGabs())
                    {
                        Write(port, token);
                    }

                    return;
                }

                Thread.Sleep(500);
            }
        }
        catch (Exception e)
        {
            Log.Warning($"[BridgeTools] Couldn't save the bridge's connection to {FilePath}: {e.Message}");
        }
    }

    private static void Write(
        int port,
        string token
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var started = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("o");
        File.WriteAllText(FilePath,
            $"{{\n  \"pid\": {Environment.ProcessId},\n  \"port\": {port},\n  \"token\": \"{token}\",\n  \"processStartedAt\": \"{started}\"\n}}\n");
        Log.Message($"[BridgeTools] Saved the bridge's port and token to {FilePath}");
    }

    // RimBridgeStartup._server, a Lib.GAB GabpServer with Port and Token
    private static (int Port, string Token) Server()
    {
        var startup = FindType("RimBridgeServer.RimBridgeStartup");
        var server = startup?.GetField("_server", AnyStatic)?.GetValue(null);
        if (server == null)
        {
            return (0, null);
        }

        var type = server.GetType();
        var port = type.GetProperty("Port", AnyInstance)?.GetValue(server) is int p ? p : 0;
        var token = type.GetProperty("Token", AnyInstance)?.GetValue(server) as string;
        return (port, token);
    }

    private static bool UnderGabs() => FindType("Lib.GAB.Gabp")?.GetMethod("IsRunningUnderGabs", AnyStatic, Type.EmptyTypes)?.Invoke(null, null) is true;

    private static Type FindType(
        string fullName
    ) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName, false)).FirstOrDefault(t => t != null);
}
