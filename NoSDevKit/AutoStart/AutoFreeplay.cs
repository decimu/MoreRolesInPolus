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
    private enum State { WaitMenu, OpenLocalMenu, ClickHostLocal, WaitCreateOptions, WaitLobby, WaitCanStart, WaitInGame, Done }

    private static State state = State.WaitMenu;
    private static float stateTime;
    private static int dummies;

    private static Assembly? nebula;

    public static void Start(int numOfDummies)
    {
        dummies = numOfDummies;
        MainThread.OnUpdate += Tick;
        Plugin.Logger.LogInfo($"AutoFreeplay: 起動後にフリープレイを開始します（ダミー {dummies} 人。起動中に Shift を押すと中止）");
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
                    SetState(State.WaitCanStart);
                }
                else if (Elapsed > 30f) Abort("ロビーに入れませんでした");
                break;

            case State.WaitCanStart:
                // 設定の反映と NoS の開始可否チェックを待つ
                if (Elapsed > 1f && GetNebulaStatic("Nebula.Patches.GameStartManagerUpdatePatch", "LastChecked") is true)
                {
                    Plugin.Logger.LogInfo("AutoFreeplay: ゲームを開始します");
                    GameStartManager.Instance.BeginGame();
                    SetState(State.WaitInGame);
                }
                else if (Elapsed > 15f) Abort("ゲームを開始できる状態になりませんでした");
                break;

            case State.WaitInGame:
                // カウントダウンとマップの読み込みを待ってから結果を報告する
                if (ShipStatus.Instance != null && LobbyBehaviour.Instance == null && Elapsed > 3f)
                {
                    Plugin.Logger.LogMessage($"AutoFreeplay: フリープレイを開始しました（プレイヤー {PlayerControl.AllPlayerControls.Count} 人）");
                    SetState(State.Done);
                    MainThread.OnUpdate -= Tick;
                }
                else if (Elapsed > 40f) Abort("ゲームが始まりませんでした");
                break;
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

        var freeplay = GameModes.AllGameModes.ToList().IndexOf(GameModes.FreePlay);
        if (gameMode.GetValue() != freeplay) gameMode.UpdateValue(freeplay);
        if (numOfDummies.GetValue() != dummies) numOfDummies.UpdateValue(dummies);

        Plugin.Logger.LogInfo($"AutoFreeplay: ゲームモードをフリープレイ、ダミーを {dummies} 人に設定しました");
        return true;
    }
}
