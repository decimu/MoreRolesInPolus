using System.Reflection;
using InnerNet;
using UnityEngine;
using Virial.Configuration;
using Virial.Game;

namespace NoSDevKit.AutoStart;

/// <summary>
/// 起動後にローカルロビーを作り、NoS のフリープレイモードでゲームを開始する。
/// 人がクリックするのと同じ順にゲームのメソッドを呼ぶだけで、NoS 側の処理（ダミーの追加など）はそのまま動く。
/// </summary>
internal static class AutoFreeplay
{
    private enum State { WaitMenu, OpenLocalMenu, ClickHostLocal, WaitCreateOptions, WaitLobby, WaitCanStart, WaitInGame, ApplyRole, Done }

    private static State state = State.WaitMenu;
    private static float stateTime;
    private static int dummies;
    private static string startRole = "";
    private static string[] dummyRoles = [];
    private static bool standard;
    private static string lobbyOptions = "";
    private static string onlyRoles = "";

    private static Assembly? nebula;

    /// <param name="mode">"freeplay"（ダミーとフリープレイ）か "standard"（複数起動して通常モードで遊ぶ）</param>
    public static void Start(string mode, int numOfDummies, string role, string dummyRoleList, string options, string onlyRoleList)
    {
        standard = mode.Trim().Equals("standard", StringComparison.OrdinalIgnoreCase);
        dummies = numOfDummies;
        startRole = role.Trim();
        dummyRoles = dummyRoleList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        lobbyOptions = options;
        onlyRoles = onlyRoleList;
        if (standard) LobbyTurn.Reset();
        MainThread.OnUpdate += Tick;
        Plugin.Logger.LogInfo(standard
            ? "AutoFreeplay: 起動後にローカルの部屋を立て、起動しているゲームが全員そろったら通常モードで開始します（起動中に Shift を押すと中止）"
            : $"AutoFreeplay: 起動後にフリープレイを開始します（ダミー {dummies} 人。起動中に Shift を押すと中止）");
    }

    /// <summary>通常モードで待つ人数。同時に起動している Among Us の数</summary>
    private static int ExpectedPlayers
    {
        get
        {
            using var me = System.Diagnostics.Process.GetCurrentProcess();
            return System.Diagnostics.Process.GetProcessesByName(me.ProcessName).Length;
        }
    }

    private static void SetState(State next)
    {
        state = next;
        stateTime = Time.realtimeSinceStartup;
    }

    private static float Elapsed => Time.realtimeSinceStartup - stateTime;

    private static void Abort(string reason)
    {
        Plugin.Logger.LogWarning("AutoFreeplay: 中止しました。" + reason);
        SetState(State.Done);
        MainThread.OnUpdate -= Tick;
    }

    private static object? GetNebulaStatic(string typeName, string memberName)
    {
        nebula ??= AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Nebula");
        var type = nebula?.GetType(typeName);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        return type?.GetField(memberName, flags)?.GetValue(null) ?? type?.GetProperty(memberName, flags)?.GetValue(null);
    }

    private static bool NoSPreprocessFinished => GetNebulaStatic("Nebula.Modules.NebulaPreprocessorImpl", "Finished") is true;

    private static void Tick()
    {
        if (state == State.Done) return;

        if (state is State.WaitMenu or State.OpenLocalMenu &&(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
        {
            Abort("Shift キーが押されていました");
            return;
        }

        switch (state)
        {
            case State.WaitMenu:
                // メニューの演出が落ち着くまで少し待つ
                if (NoSPreprocessFinished && UnityEngine.Object.FindObjectOfType<MainMenuManager>() != null) SetState(State.OpenLocalMenu);
                break;

            case State.OpenLocalMenu:
                if (Elapsed < 1.5f) break;
                var menu = UnityEngine.Object.FindObjectOfType<MainMenuManager>();
                if (menu == null || menu.playLocalButton == null)
                {
                    Abort("メインメニューの「ローカル」ボタンが見つかりません");
                    break;
                }
                // NoS はこのクリックで「ローカルゲームかどうか」を記録するので、ボタン経由で押す
                Plugin.Logger.LogInfo("AutoFreeplay: ローカルメニューを開きます");
                menu.playLocalButton.OnClick.Invoke();
                SetState(State.ClickHostLocal);
                break;

            case State.ClickHostLocal:
                var button = UnityEngine.Object.FindObjectsOfType<HostLocalGameButton>(true).FirstOrDefault(b => b.NetworkMode == NetworkModes.LocalGame && b.gameObject.activeInHierarchy);
                if (button == null || Elapsed < 1f)
                {
                    if (Elapsed > 10f) Abort("ローカルゲームの作成ボタンが見つかりません");
                    break;
                }
                Plugin.Logger.LogInfo("AutoFreeplay: ローカルゲームを作成します");
                button.OnClick();
                SetState(State.WaitCreateOptions);
                break;

            case State.WaitCreateOptions:
                if (LobbyBehaviour.Instance != null || AmongUsClient.Instance.AmConnected)
                {
                    SetState(State.WaitLobby);
                    break;
                }
                var options = UnityEngine.Object.FindObjectOfType<CreateGameOptions>();
                if (options != null && options.gameObject.activeInHierarchy && Elapsed > 1f)
                {
                    options.Confirm();
                    SetState(State.WaitLobby);
                }
                else if (Elapsed > 15f) Abort("ゲーム作成画面が開きませんでした");
                break;

            case State.WaitLobby:
                if (LobbyBehaviour.Instance != null && PlayerControl.LocalPlayer != null && GameStartManager.InstanceExists && AmongUsClient.Instance.AmHost)
                {
                    if (!ApplyFreeplayOptions()) break;
                    // 2 つ目以降のゲームに「部屋ができた（1 人目が入った）」と伝える
                    if (standard) LobbyTurn.MarkJoined(1);
                    SetState(State.WaitCanStart);
                }
                else if (Elapsed > 30f) Abort("ロビーに入れませんでした");
                break;

            case State.WaitCanStart:
                // 設定の反映と NoS の開始可否チェックを待つ。通常モードでは、起動しているゲームが全員そろうのも待つ
                if (standard && (PlayerControl.AllPlayerControls.Count < ExpectedPlayers || LobbyTurn.Joined < ExpectedPlayers))
                {
                    if (Elapsed > 180f) Abort($"人がそろいませんでした（{PlayerControl.AllPlayerControls.Count}/{ExpectedPlayers} 人）");
                    break;
                }
                if (Elapsed > 1f && GetNebulaStatic("Nebula.Patches.GameStartManagerUpdatePatch", "LastChecked") is true)
                {
                    Plugin.Logger.LogInfo("AutoFreeplay: ゲームを開始します");
                    // BeginGame で NoS がダミーを追加する。そのあと F1（クイックスタート）と同じくカウントダウンを飛ばす
                    var gameStartManager = GameStartManager.Instance;
                    gameStartManager.BeginGame();
                    if (gameStartManager.startState == GameStartManager.StartingStates.Countdown) gameStartManager.FinallyBegin();
                    SetState(State.WaitInGame);
                }
                else if (Elapsed > (standard ? 195f : 15f)) Abort("ゲームを開始できる状態になりませんでした");
                break;

            case State.WaitInGame:
                // カウントダウンとマップの読み込みを待ってから結果を報告する
                if (ShipStatus.Instance != null && LobbyBehaviour.Instance == null && Elapsed > 3f)
                {
                    Plugin.Logger.LogMessage($"AutoFreeplay: {(standard ? "通常モード" : "フリープレイ")}を開始しました（プレイヤー {PlayerControl.AllPlayerControls.Count} 人）");
                    // 通常モードでは、ゲーム本来の役職の割り当てを確かめたいので役職は変えない
                    SetState(standard || (startRole.Length == 0 && dummyRoles.Length == 0) ? State.Done : State.ApplyRole);
                    if (state == State.Done) MainThread.OnUpdate -= Tick;
                }
                else if (Elapsed > 40f) Abort("ゲームが始まりませんでした");
                break;

            case State.ApplyRole:
                // 役職の割り当てとイントロ演出が終わってから変えないと、上書きされたり自分の情報がまだ無かったりする
                if (IntroCutscene.Instance == null && Virial.NebulaAPI.CurrentGame?.LocalPlayer?.Role != null && Elapsed > 1f)
                {
                    ApplyStartRole();
                    SetState(State.Done);
                    MainThread.OnUpdate -= Tick;
                }
                else if (Elapsed > 30f) Abort("自分のプレイヤー情報が用意されないため、役職を変えられませんでした");
                break;
        }
    }

    /// <summary>設定された役職に自分とダミーを変える（試したい役職の組み合わせですぐ始められるように）</summary>
    private static void ApplyStartRole()
    {
        bool TrySet(Virial.Game.Player player, string spec)
        {
            try
            {
                var (role, arguments) = RoleSpec.Parse(spec);
                player.SetRole(role, arguments);
                return true;
            }
            catch (ArgumentException e)
            {
                Plugin.Logger.LogWarning("AutoFreeplay: " + e.Message);
                return false;
            }
        }

        var me = Virial.NebulaAPI.CurrentGame!.LocalPlayer;
        if (startRole.Length > 0 && TrySet(me, startRole))
            Plugin.Logger.LogMessage($"AutoFreeplay: 自分の役職を {startRole} にしました");

        var others = Virial.Game.Player.AllPlayers.Where(p => p != me).ToArray();
        for (int i = 0; i < dummyRoles.Length && i < others.Length; i++)
        {
            if (TrySet(others[i], dummyRoles[i]))
                Plugin.Logger.LogMessage($"AutoFreeplay: ダミー（ID {others[i].PlayerId}）の役職を {dummyRoles[i]} にしました");
        }
    }

    /// <summary>NoS のゲームモードをフリープレイにし、ダミーの人数を設定する</summary>
    private static bool ApplyFreeplayOptions()
    {
        const string configs = "Nebula.Configuration.GeneralConfigurations";
        if (GetNebulaStatic(configs, "GameModeOption") is not ValueConfiguration<int> gameMode
            || GetNebulaStatic(configs, "NumOfDummiesOption") is not ValueConfiguration<int> numOfDummies)
        {
            Abort("NoS の設定項目が見つかりません");
            return false;
        }

        var target = GameModes.AllGameModes.ToList().IndexOf(standard ? GameModes.Standard : GameModes.FreePlay);
        if (gameMode.GetValue() != target) gameMode.UpdateValue(target);
        if (!standard && numOfDummies.GetValue() != dummies) numOfDummies.UpdateValue(dummies);
        NoSOptions.OnlyRoles(onlyRoles, "AutoFreeplay");
        NoSOptions.SetAll(lobbyOptions, "AutoFreeplay");

        Plugin.Logger.LogInfo(standard
            ? $"AutoFreeplay: ゲームモードを通常モードに設定しました。{ExpectedPlayers} 人そろうのを待ちます"
            : $"AutoFreeplay: ゲームモードをフリープレイ、ダミーを {dummies} 人に設定しました");
        return true;
    }
}
