using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;

namespace NoSDevKit.Logging;

/// <summary>
/// NoS が書き出す NebulaLog.txt を追いかけて LogHub に流す
/// </summary>
internal static class NebulaLogTailer
{
    // 例: [Warning | Scripting] Compile Error! ...
    private static readonly Regex header = new(@"^\[(\w+)\s*\|\s*([^\]]+)\]\s?(.*)$", RegexOptions.Compiled);

    private static Level? pendingLevel;
    private static string pendingSource = "";
    private static readonly StringBuilder pendingText = new();
    private static DateTime pendingTime;

    public static void Start()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "NoSDevKit.NebulaLogTailer" };
        thread.Start();
    }

    /// <summary>
    /// NoS が実際に開いたログファイル。複数起動すると "NebulaLog 1.txt" のように空いている番号を使うので、NoS 自身の値を読む
    /// </summary>
    private static string? ResolvePath()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Nebula")?.GetType("Nebula.NebulaLogFile");
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        if (type?.GetField("writer", flags)?.GetValue(null) == null) return null;
        var number = type.GetField("number", flags)?.GetValue(null) as int? ?? 0;
        return Path.Combine(Paths.GameRootPath, number == 0 ? "NebulaLog.txt" : $"NebulaLog {number}.txt");
    }

    private static void Run()
    {
        string? path;
        while ((path = ResolvePath()) == null) Thread.Sleep(200);

        long position = 0;
        var buffer = new StringBuilder();
        var bytes = new byte[64 * 1024];
        var decoder = Encoding.UTF8.GetDecoder();
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];

        while (true)
        {
            try
            {
                if (File.Exists(path))
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    // NoS が作り直した（短くなった）ら先頭から読み直す
                    if (stream.Length < position) { position = 0; buffer.Clear(); }
                    stream.Seek(position, SeekOrigin.Begin);

                    int read;
                    while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
                    {
                        int n = decoder.GetChars(bytes, 0, read, chars, 0);
                        buffer.Append(chars, 0, n);
                        position += read;
                    }

                    var text = buffer.ToString();
                    var lastNewline = text.LastIndexOf('\n');
                    if (lastNewline >= 0)
                    {
                        foreach (var line in text.Substring(0, lastNewline).Split('\n')) Feed(line.TrimEnd('\r'));
                        buffer.Remove(0, lastNewline + 1);
                    }
                }

                // 続きの行が来ないまましばらく経ったら、溜めていた 1 件を確定させる
                if (pendingLevel != null && (DateTime.Now - pendingTime).TotalMilliseconds > 300) Flush();
            }
            catch (Exception e)
            {
                LogHub.Write(new LogEntry(DateTime.Now, Level.Warning, "NoSDevKit", "NebulaLog.txt を読めませんでした: " + e.Message));
                Thread.Sleep(2000);
            }
            Thread.Sleep(200);
        }
    }

    private static void Feed(string line)
    {
        var m = header.Match(line.TrimStart('﻿'));
        if (m.Success)
        {
            Flush();
            pendingLevel = m.Groups[1].Value.ToLowerInvariant() switch
            {
                "error" or "fatal" => Level.Error,
                "warning" => Level.Warning,
                "message" => Level.Message,
                "info" => Level.Info,
                _ => Level.Debug,
            };
            pendingSource = "NoS:" + m.Groups[2].Value.Trim();
            pendingText.Clear().Append(m.Groups[3].Value);
            pendingTime = DateTime.Now;
        }
        else if (pendingLevel != null && line.Length > 0)
        {
            pendingText.Append('\n').Append(line.TrimStart());
            pendingTime = DateTime.Now;
        }
    }

    private static void Flush()
    {
        if (pendingLevel == null) return;
        LogHub.Write(new LogEntry(pendingTime, pendingLevel.Value, pendingSource, pendingText.ToString()));
        pendingLevel = null;
    }
}
