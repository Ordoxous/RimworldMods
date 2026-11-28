using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RimBridgeServer.Sdk;
using Verse;

namespace Ordo.BridgeTools;

// Which mods are throwing: the game's errors (and warnings, if asked) grouped by the mod they most likely come from, with
// counts, first and last time, and an example. Attribution (shared with ordo/origin) takes the first mod frame of the
// stack trace, else a "[Tag]" the text starts with, else the game with the Harmony owners that patched the frames.
public sealed class ErrorTools
{
    private sealed record Entry(string Level, string Time, int Repeats, string Text, string Stack);

    [Tool("ordo/errors_by_mod", Description = "The game's errors (and warnings with includeWarnings) grouped by the mod they most likely come from: count, first and last time, and an example message. Reads the game's own log queue (its last 1000 entries), or with wholeSession this game's error mirror file, which holds every warning and error since it started.")]
    public static object ErrorsByMod(
        [ToolParameter(Description = "Group warnings too")] bool includeWarnings = false,
        [ToolParameter(Description = "Read this game's whole session from its error mirror file instead of the log queue")] bool wholeSession = false,
        [ToolParameter(Description = "How many groups")] int limit = 25
    )
    {
        var entries = wholeSession ? FromMirror(ErrorMirror.FilePath) : FromQueue();
        var groups = entries
            .Where(e => e.Level == "Error" || (includeWarnings && e.Level == "Warning"))
            .Select(e => (Entry: e, Mod: Attribution.FromLogged(e.Text, e.Stack)))
            .GroupBy(x => x.Mod)
            .Select(g => new
            {
                mod = g.Key,
                count = g.Sum(x => Math.Max(1, x.Entry.Repeats)),
                distinct = g.Select(x => FirstLine(x.Entry.Text)).Distinct().Count(),
                first = g.First().Entry.Time,
                last = g.Last().Entry.Time,
                example = Shorten(FirstLine(g.Last().Entry.Text), 240)
            })
            .OrderByDescending(g => g.count)
            .Take(limit)
            .ToList();
        return new { success = true, source = wholeSession ? ErrorMirror.FilePath : "log queue", entries = entries.Count, groups };
    }

    private static List<Entry> FromQueue() => Verse.Log.Messages
        .Where(m => m.type != LogMessageType.Message)
        .Select(m => new Entry(m.type.ToString(), m.timestamp, m.repeats, m.text, m.StackTrace))
        .ToList();

    // The mirror's lines: "time LEVEL [note] [(xN)] text", then an error's stack indented by four spaces
    private static readonly Regex Head = new(@"^(?<time>\S+)\s+(?<level>WARNING|ERROR)(?:\s+\(before the mirror\))?(?:\s+\(x(?<repeats>\d+)\))?\s(?<text>.*)$", RegexOptions.Compiled);

    private static List<Entry> FromMirror(
        string path
    )
    {
        var entries = new List<Entry>();
        if (!File.Exists(path))
        {
            return entries;
        }

        string time = null, level = null, text = null;
        var repeats = 1;
        var stack = new List<string>();
        void Flush()
        {
            if (text != null)
            {
                entries.Add(new Entry(level == "ERROR" ? "Error" : "Warning", time, repeats, text, string.Join("\n", stack)));
            }

            text = null;
            stack.Clear();
        }

        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("    "))
            {
                stack.Add(line);
                continue;
            }

            var head = Head.Match(line);
            if (!head.Success)
            {
                continue;
            }

            Flush();
            time = head.Groups["time"].Value;
            level = head.Groups["level"].Value;
            repeats = head.Groups["repeats"].Success ? int.Parse(head.Groups["repeats"].Value) : 1;
            text = head.Groups["text"].Value;
        }

        Flush();
        return entries;
    }

    private static string FirstLine(
        string text
    ) => (text ?? "").Split('\n')[0].Trim();

    private static string Shorten(
        string text,
        int max
    ) => text.Length <= max ? text : text.Substring(0, max) + "…";
}
