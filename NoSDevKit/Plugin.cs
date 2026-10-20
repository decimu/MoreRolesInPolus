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
        // 複数起動したとき、コスメ画像のキャッシュへの同時アクセスや帽子の描画で大量に出る（動作には影響しない）
        @"|Il2CppInterop (Exception in IL2CPP-to-Managed trampoline|During invoking native->managed trampoline)[\s\S]*(MoreCosmic|HatParent)" +
        @"|Unity (UseButtonSettings must|Can't add component 'SortingGroup')" +
        @"|NoS:General .*( is registered\.| doesn't have preprocessor|Try Instantiate Sync))";

    internal static ConfigEntry<bool> HotReloadEnabled = null!;
    internal static ConfigEntry<string> AddonSourceDir = null!;
    internal static ConfigEntry<bool> AutoFreeplayEnabled = null!;
    internal static ConfigEntry<string> AutoFreeplayMode = null!;
    internal static ConfigEntry<string> AutoFreeplayOptions = null!;
    internal static ConfigEntry<string> AutoFreeplayOnlyRoles = null!;
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
        AutoFreeplayMode = Config.Bind("AutoFreeplay", "Mode", "freeplay", new ConfigDescription("freeplay: ダミーとフリープレイ / standard: 複数起動して 1 つ目が部屋を立て、2 つ目以降が参加し、全員そろったら通常モードで開始", new AcceptableValueList<string>("freeplay", "standard")));
        AutoFreeplayOptions = Config.Bind("AutoFreeplay", "Options", "", "ロビーで変える NoS の設定（id=値 をセミコロン区切り。例: role.autocracy.count=1;role.autocracy.chance=100）");
        AutoFreeplayOnlyRoles = Config.Bind("AutoFreeplay", "OnlyRoles", "", "ロビーで、ここに書いた役職（カンマ区切りの内部名）以外の出現人数を 0 にする。変える前の値は DevKit.roles.bak に残り、restoreroles 命令で戻せる");
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

        // 部屋を立てるのは最初の 1 つだけ。通常モードでは 2 つ目以降がその部屋に参加する
        if (AutoFreeplayEnabled.Value && Instance.IsFirst)
            AutoFreeplay.Start(AutoFreeplayMode.Value, AutoFreeplayDummies.Value, AutoFreeplayRole.Value, AutoFreeplayDummyRoles.Value, AutoFreeplayOptions.Value, AutoFreeplayOnlyRoles.Value);
        else if (AutoFreeplayEnabled.Value && AutoFreeplayMode.Value.Trim().Equals("standard", StringComparison.OrdinalIgnoreCase))
            AutoJoin.Start();
        else if (AutoFreeplayEnabled.Value)
            Log.LogInfo($"AutoFreeplay: {Instance.Index} 番目に起動したゲームなので行いません");

        if (HotReloadEnabled.Value)
        {
            if (string.IsNullOrWhiteSpace(AddonSourceDir.Value)) Log.LogWarning($"HotReload: AddonSourceDir が未設定です（{Config.ConfigFilePath}）");
            else HotReloader.Start(AddonSourceDir.Value);
        }

        Log.LogInfo($"NoSDevKit loaded. ログ: {LogHub.LogPath}");
    }
}
