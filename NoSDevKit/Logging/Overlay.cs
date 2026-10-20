using System.Reflection;
using System.Text.RegularExpressions;

namespace NoSDevKit.Logging;

/// <summary>
/// 警告・エラーと NoSDevKit 自身の通知を、NoS の DebugScreen（画面左上）に一時表示する
/// </summary>
internal static class Overlay
{
    private const int MaxLines = 4;
    private const int MaxLineLength = 160;

    private static readonly Regex scriptsPath = new(@"^[A-Za-z]:\\.*?\\Scripts\\", RegexOptions.Compiled);

    private static MethodInfo? push;
    private static readonly Dictionary<string, DateTime> recent = [];
    private static DateTime windowStart;
    private static int windowCount;

    public static void Start()
    {
        LogHub.OnEntry += entry =>
        {
            bool fromDevKit = entry.Source == "NoSDevKit";
            if (entry.Level < Level.Warning && !(fromDevKit && entry.Level >= Level.Message)) return;
            MainThread.Post(() => Show(entry));
        };
    }

    private static void Show(LogEntry entry)
    {
        var now = DateTime.Now;

        // 毎フレーム出るような例外で画面が埋まらないようにする
        if (recent.TryGetValue(entry.Text, out var last) && (now - last).TotalSeconds < 5) return;
        recent[entry.Text] = now;
        if ((now - windowStart).TotalSeconds >= 1) { windowStart = now; windowCount = 0; }
        if (++windowCount > 5) return;

        push ??= AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Nebula")
            ?.GetType("Nebula.Modules.DebugScreen")
            ?.GetMethod("Push", BindingFlags.Static | BindingFlags.Public, [typeof(string), typeof(float)]);
        if (push == null) return;

        var (color, duration) = entry.Level switch
        {
            Level.Error => ("#FF6B6B", 15f),
            Level.Warning => ("#FFD166", 8f),
            _ => ("#7BDFF2", 5f),
        };

        var lines = entry.Text.Replace("\r", "").Split('\n')
            // 画面では絶対パスが長すぎるので Scripts\ 以下だけにする
            .Select(l => scriptsPath.Replace(l, @"Scripts\"))
            .Select(l => l.Length > MaxLineLength ? l.Substring(0, MaxLineLength) + "…" : l)
            .Take(MaxLines);
        // TextMeshPro のタグとして解釈されないように < をエスケープする
        var body = string.Join("\n", lines).Replace("<", "<​");

        try { push.Invoke(null, [$"<color={color}>[{entry.Source}]</color> {body}", duration]); }
        catch { }
    }
}
