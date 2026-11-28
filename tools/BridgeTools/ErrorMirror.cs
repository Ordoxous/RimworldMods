using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using Verse;

namespace Ordo.BridgeTools;

// Every warning and error of a game, in ~/.gabs/rimworld/logs/<pid>.log. Each RimWorld (test games too) starts a new
// Player.log in the same place, and the game's own queue keeps only 1000 entries, so a long session's errors were lost to
// the next test game. Started by BridgeConnection when the bridge discovers these tools: what the queue already holds is
// copied first, then each new warning or error is appended as it's logged. A repeat the game merges into the last entry
// isn't written again. The 20 newest files are kept.
public static class ErrorMirror
{
    private static readonly object Sync = new();
    private static StreamWriter writer;
    private static AccessTools.FieldRef<LogMessageQueue, LogMessage> lastMessage;

    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gabs", "rimworld", "logs");

    public static string FilePath => Path.Combine(Directory, $"{Environment.ProcessId}.log");

    public static void Start()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            foreach (var old in new DirectoryInfo(Directory).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Skip(19))
            {
                old.Delete();
            }

            lock (Sync)
            {
                writer = new StreamWriter(new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                writer.WriteLine($"=== RimWorld pid {Environment.ProcessId}, process started {System.Diagnostics.Process.GetCurrentProcess().StartTime:o}, mirror started {DateTime.Now:o}");
                foreach (var message in Verse.Log.Messages.Where(m => m.type != LogMessageType.Message).ToList())
                {
                    Write(message, "(before the mirror)");
                }
            }

            lastMessage = AccessTools.FieldRefAccess<LogMessageQueue, LogMessage>("lastMessage");
            new Harmony("Ordo.BridgeTools.ErrorMirror").Patch(
                AccessTools.Method(typeof(LogMessageQueue), nameof(LogMessageQueue.Enqueue), new[] { typeof(LogMessage), typeof(bool).MakeByRefType() }),
                postfix: new HarmonyMethod(typeof(ErrorMirror), nameof(Postfix_Enqueue)));
        }
        catch (Exception e)
        {
            Verse.Log.Warning($"[BridgeTools] Couldn't start the error mirror in {Directory}: {e.Message}");
        }
    }

    public static void Postfix_Enqueue(
        LogMessageQueue __instance,
        LogMessage msg
    )
    {
        // A repeat was merged into the last entry rather than queued
        if (msg.type == LogMessageType.Message || lastMessage(__instance) != msg)
        {
            return;
        }

        lock (Sync)
        {
            Write(msg, null);
        }
    }

    private static void Write(
        LogMessage message,
        string note
    )
    {
        try
        {
            writer?.WriteLine($"{message.timestamp:yyyy-MM-dd HH:mm:ss.fff} {message.type.ToString().ToUpperInvariant()}{(note == null ? "" : " " + note)}{(message.repeats > 1 ? $" (x{message.repeats})" : "")} {message.text}");
            if (message.type == LogMessageType.Error && !string.IsNullOrEmpty(message.StackTrace))
            {
                writer?.WriteLine(string.Join("\n", message.StackTrace.Split('\n').Take(25).Select(line => "    " + line.TrimEnd())));
            }
        }
        catch
        {
            // Never let the mirror throw inside the game's logging
        }
    }
}
