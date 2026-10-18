using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using NoSDevKit.AutoStart;
using NoSDevKit.HotReload;
using NoSDevKit.Logging;

namespace NoSDevKit;

[BepInPlugin(Guid, "NoSDevKit", "0.1.0")]
public class Plugin : BasePlugin
{
    public const string Guid = "dev.nosdevkit";

    internal static ManualLogSource Logger = null!;

    /// <summary>起動のたびに大量に出るが、アドオン開発では見る必要のないもの</summary>
    private const string DefaultIgnorePattern =
        @"^(NoS:MoreCosmic|Console Failed \(Get|DobbyDetour " +
        @"|Il2CppInterop (Method .* has unsupported|Registered mono type|Original class was inflated)" +
        @"|Unity (UseButtonSettings must|Can't add component 'SortingGroup')" +
        @"|NoS:General .*( is registered\.| doesn't have preprocessor|Try Instantiate Sync))";

    internal static ConfigEntry<bool> HotReloadEnabled = null!;
    internal static ConfigEntry<string> AddonSourceDir = null!;
    internal static ConfigEntry<bool> AutoFreeplayEnabled = null!;
    internal static ConfigEntry<int> AutoFreeplayDummies = null!;
    internal static ConfigEntry<string> AutoFreeplayRole = null!;
    internal static ConfigEntry<string> AutoFreeplayDummyRoles = null!;
    internal static ConfigEntry<string> LogIgnorePattern = null!;
    internal static ConfigEntry<bool> LogOverlay = null!;

    public override void Load()
    {
        Logger = Log;

        HotReloadEnabled = Config.Bind("HotReload", "Enabled", true, "スクリプト保存時にメソッド本体を差し替える");
        AddonSourceDir = Config.Bind("HotReload", "AddonSourceDir", "", "addon.meta を直接含むアドオンのソースフォルダ（例: D:\\.Project\\MyAddon\\MyAddon）");
        AutoFreeplayEnabled = Config.Bind("AutoFreeplay", "Enabled", true, "起動後にローカルロビーを作り、フリープレイを自動で開始する（起動中に Shift を押すと中止）");
        AutoFreeplayDummies = Config.Bind("AutoFreeplay", "NumOfDummies", 4, new ConfigDescription("フリープレイで追加するダミーの人数", new AcceptableValueRange<int>(0, 23)));
        AutoFreeplayRole = Config.Bind("AutoFreeplay", "Role", "", "フリープレイ開始後に自分をこの役職にする（役職の内部名。例: autocracy。空なら変えない）");
        AutoFreeplayDummyRoles = Config.Bind("AutoFreeplay", "DummyRoles", "", "フリープレイ開始後にダミーをこの役職にする（カンマ区切りで先頭のダミーから順に。例: rebel,impostor）");
        LogIgnorePattern = Config.Bind("Log", "IgnorePattern", DefaultIgnorePattern, "DevKit.log とゲーム内表示から除く行の正規表現（「出所 本文」に対して判定）");
        LogOverlay = Config.Bind("Log", "Overlay", true, "警告・エラーと NoSDevKit の通知をゲーム画面の左上に表示する");

        // 他の機能のログも拾えるように最初に始める
        LogHub.Start(LogIgnorePattern.Value);
        if (LogOverlay.Value) Overlay.Start();

        AddComponent<MainThread>();

        // 命令ファイルは 1 つなので、最初に起動したゲームだけが読む
        if (Instance.IsFirst) DevCommands.Start();

        // 複数起動したときに全員がロビーを作らないよう、最初の 1 つだけで行う
        if (AutoFreeplayEnabled.Value && Instance.IsFirst) AutoFreeplay.Start(AutoFreeplayDummies.Value, AutoFreeplayRole.Value, AutoFreeplayDummyRoles.Value);
        else if (AutoFreeplayEnabled.Value) Log.LogInfo($"AutoFreeplay: {Instance.Index} 番目に起動したゲームなので行いません");

        if (HotReloadEnabled.Value)
        {
            if (string.IsNullOrWhiteSpace(AddonSourceDir.Value)) Log.LogWarning($"HotReload: AddonSourceDir が未設定です（{Config.ConfigFilePath}）");
            else HotReloader.Start(AddonSourceDir.Value);
        }

        Log.LogInfo($"NoSDevKit loaded. ログ: {LogHub.LogPath}");
    }
}
