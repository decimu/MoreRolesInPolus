using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Logging;

namespace NoSDevKit.Logging;

internal enum Level { Debug, Info, Message, Warning, Error }

internal record LogEntry(DateTime Time, Level Level, string Source, string Text);

/// <summary>
/// BepInEx のログと NoS の NebulaLog.txt を 1 つの DevKit.log にまとめる
/// </summary>
internal static class LogHub
{
    private static readonly object writeLock = new();
    private static StreamWriter? writer;
    private static Regex? ignore;

    /// <summary>ノイズを除いたログが届くたびに呼ばれる（呼び出し元のスレッドは不定）</summary>
    public static event Action<LogEntry>? OnEntry;

    /// <summary>複数起動したときは 2 つ目以降を DevKit.2.log のように分ける</summary>
    public static string LogPath => Path.Combine(Paths.GameRootPath, Instance.IsFirst ? "DevKit.log" : $"DevKit.{Instance.Index}.log");

    public static void Start(string ignorePattern)
    {
        if (!string.IsNullOrWhiteSpace(ignorePattern)) ignore = new Regex(ignorePattern, RegexOptions.Compiled);

        // 起動のたびに作り直す。ビューアは他のプロセスから読むので共有を許可する
        var stream = new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

        BepInEx.Logging.Logger.Listeners.Add(new BepInExListener());
        NebulaLogTailer.Start();
    }

    public static void Write(LogEntry entry)
    {
        if (ignore != null && ignore.IsMatch(entry.Source + " " + entry.Text)) return;

        var label = entry.Level switch
        {
            Level.Error => "ERR",
            Level.Warning => "WRN",
            Level.Message => "MSG",
            Level.Info => "INF",
            _ => "DBG",
        };
        // 2 行目以降は字下げして、1 件のログだとわかるようにする
        var text = entry.Text.TrimEnd().Replace("\r\n", "\n").Replace("\n", "\n                   ");
        var line = $"{entry.Time:HH:mm:ss.fff} {label} [{entry.Source}] {text}";

        lock (writeLock) writer?.WriteLine(line);

        try { OnEntry?.Invoke(entry); }
        catch { }
    }

    private class BepInExListener : ILogListener
    {
        public LogLevel LogLevelFilter => LogLevel.All;

        public void LogEvent(object sender, LogEventArgs args)
        {
            var level = args.Level switch
            {
                var l when (l & (LogLevel.Fatal | LogLevel.Error)) != 0 => Level.Error,
                var l when (l & LogLevel.Warning) != 0 => Level.Warning,
                var l when (l & LogLevel.Message) != 0 => Level.Message,
                var l when (l & LogLevel.Info) != 0 => Level.Info,
                _ => Level.Debug,
            };
            Write(new LogEntry(DateTime.Now, level, args.Source.SourceName.Trim(), args.Data?.ToString() ?? ""));
        }

        public void Dispose() { }
    }
}
