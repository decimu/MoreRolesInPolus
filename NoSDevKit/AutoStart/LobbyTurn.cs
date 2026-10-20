using BepInEx;

namespace NoSDevKit.AutoStart;

/// <summary>
/// 複数起動したゲームが 1 人ずつ順番に部屋へ入るための受け渡し。
/// ゲームフォルダの DevKit.lobby に「何番目まで部屋に入ったか」を書く。
/// （同時に参加しようとすると、ゲーム開始直後に参加者が落ちることがあるため）
/// </summary>
internal static class LobbyTurn
{
    private static string FilePath => Path.Combine(Paths.GameRootPath, "DevKit.lobby");

    /// <summary>部屋を立てる側が起動時に呼ぶ。前回の記録を消す</summary>
    public static void Reset()
    {
        try { File.Delete(FilePath); } catch { }
    }

    /// <summary>index 番目まで部屋に入ったことを書く</summary>
    public static void MarkJoined(int index)
    {
        try { File.WriteAllText(FilePath, index.ToString()); }
        catch (Exception e) { Plugin.Logger.LogWarning("LobbyTurn: 書き込めませんでした: " + e.Message); }
    }

    /// <summary>何番目まで部屋に入ったか（まだ誰もいなければ 0）</summary>
    public static int Joined
    {
        get
        {
            try { return int.TryParse(File.ReadAllText(FilePath).Trim(), out var n) ? n : 0; }
            catch { return 0; }
        }
    }
}
