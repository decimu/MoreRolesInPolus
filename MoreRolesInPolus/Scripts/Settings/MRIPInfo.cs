/**
 * @file MRIPInfo.cs
 * @brief MRIPアドオンの情報を一元管理
 */
namespace Toa.MoreRolesInPolus.Scripts.Settings;

/// <summary>
/// MRIPアドオンの情報を管理する静的クラス
/// addon.metaから動的に情報を取得する
/// </summary>
public static class MRIPInfo
{
    /// <summary>
    /// アドオンID（addon.metaのIdと一致）
    /// </summary>
    public const string AddonId = "more-roles-in-polus";
    
    /// <summary>
    /// 表示用の短縮名
    /// </summary>
    public const string ShortName = "MRIP";
    
    private static NebulaAddon? _cachedAddon = null;
    
    /// <summary>
    /// NebulaAddonインスタンスを取得（キャッシュ付き）
    /// </summary>
    public static NebulaAddon? Addon
    {
        get
        {
            if (_cachedAddon == null)
            {
                _cachedAddon = NebulaAddon.GetAddon(AddonId);
            }
            return _cachedAddon;
        }
    }
    
    /// <summary>
    /// アドオン名（addon.metaから取得）
    /// </summary>
    public static string AddonName => Addon?.AddonName ?? AddonId;
    
    /// <summary>
    /// バージョン番号（addon.metaから取得）
    /// </summary>
    public static string Version => Addon?.Version ?? "Unknown";
    
    /// <summary>
    /// 完全なバージョン文字列を取得
    /// CI/CDがaddon.metaのVersionを自動更新するため、そのまま使用
    /// </summary>
    /// <returns>フォーマットされたバージョン文字列</returns>
    public static string GetVersionString()
    {
        // Versionに"Snapshot"が含まれていればSnapshot版、そうでなければ正式版
        if (Version.Contains("Snapshot", System.StringComparison.OrdinalIgnoreCase))
        {
            // Snapshot版: CI が "Snapshot_26.01.05a" の形式で設定済み
            return $" + {ShortName} {Version}";
        }
        else
        {
            // 正式版: "0.1.5" → "v0.1.5" 形式に変換
            return $" + {ShortName} v{Version}";
        }
    }
    

}
