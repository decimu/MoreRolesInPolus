using System.Reflection;
using UnityEngine;

namespace NoSDevKit.AutoStart;

/// <summary>
/// 2 つ目以降に起動したゲームで、1 つ目のゲームが立てたローカルの部屋に自動で参加する。
/// 「ローカル」メニューを開くと近くの部屋の参加ボタンが並ぶので、それを押すだけ。
/// </summary>
internal static class AutoJoin
{
    private enum State { WaitMenu, WaitTurn, OpenLocalMenu, ClickJoin, WaitLobby, Settle, Done }

    /// <summary>前の人が入りきってから、部屋を探し始めるまでに置く余白（秒）</summary>
    private const float TurnMargin = 3f;

    /// <summary>部屋に入ってから、次の人に番を回すまでに置く余白（秒）</summary>
    private const float SettleMargin = 3f;

    private static State state = State.WaitMenu;
    private static float stateTime;
    private static float? turnSince;

    public static void Start()
    {
        MainThread.OnUpdate += Tick;
        Plugin.Logger.LogInfo($"AutoJoin: {Instance.Index} 番目のゲームとして、ローカルの部屋に自動で参加します（起動中に Shift を押すと中止）");
    }

    private static void SetState(State next)
    {
        state = next;
        stateTime = Time.realtimeSinceStartup;
    }

    private static float Elapsed => Time.realtimeSinceStartup - stateTime;

    private static void Finish(string message, bool warning)
    {
        if (warning) Plugin.Logger.LogWarning("AutoJoin: 中止しました。" + message);
        else Plugin.Logger.LogMessage("AutoJoin: " + message);
        SetState(State.Done);
        MainThread.OnUpdate -= Tick;
    }

    private static bool NoSPreprocessFinished =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Nebula")
            ?.GetType("Nebula.Modules.NebulaPreprocessorImpl")
            ?.GetProperty("Finished", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(null) is true;

    private static void Tick()
    {
        if (state == State.Done) return;

        if (state is State.WaitMenu or State.WaitTurn or State.OpenLocalMenu && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
        {
            Finish("Shift キーが押されていました", true);
            return;
        }

        switch (state)
        {
            case State.WaitMenu:
                if (NoSPreprocessFinished && UnityEngine.Object.FindObjectOfType<MainMenuManager>() != null)
                {
                    Plugin.Logger.LogInfo($"AutoJoin: {Instance.Index - 1} 番目までが部屋に入るのを待ちます");
                    SetState(State.WaitTurn);
                }
                break;

            case State.WaitTurn:
                // 同時に参加すると開始直後に落ちることがあるので、前の人が入りきってから余白を置いて部屋を探す
                if (LobbyTurn.Joined >= Instance.Index - 1)
                {
                    turnSince ??= Time.realtimeSinceStartup;
                    if (Time.realtimeSinceStartup - turnSince.Value >= TurnMargin) SetState(State.OpenLocalMenu);
                }
                else if (Elapsed > 300f) Finish("前の人が部屋に入るのを待ちきれませんでした", true);
                break;

            case State.OpenLocalMenu:
                if (Elapsed < 1.5f) break;
                var menu = UnityEngine.Object.FindObjectOfType<MainMenuManager>();
                if (menu == null || menu.playLocalButton == null)
                {
                    Finish("メインメニューの「ローカル」ボタンが見つかりません", true);
                    break;
                }
                Plugin.Logger.LogInfo("AutoJoin: ローカルメニューを開いて部屋を探します");
                menu.playLocalButton.OnClick.Invoke();
                SetState(State.ClickJoin);
                break;

            case State.ClickJoin:
                // 1 つ目のゲームが部屋を立てるまで待つ。見つかった部屋ごとに GameDiscovery の一覧へ参加ボタンが作られる。
                // メニューにはオンライン用の参加ボタンも隠れて存在するので、一覧の中のボタンだけを対象にする
                var discovery = UnityEngine.Object.FindObjectOfType<GameDiscovery>();
                JoinGameButton? join = null;
                if (discovery != null && discovery.ItemLocation != null)
                {
                    join = discovery.ItemLocation.GetComponentsInChildren<JoinGameButton>(false)
                        .FirstOrDefault(b => b.gameObject.activeInHierarchy && !string.IsNullOrEmpty(b.netAddress));
                }
                if (join == null)
                {
                    if (Elapsed > 120f) Finish("ローカルの部屋が見つかりませんでした", true);
                    break;
                }
                Plugin.Logger.LogInfo($"AutoJoin: 部屋に参加します（{join.netAddress}）");
                join.OnClick();
                SetState(State.WaitLobby);
                break;

            case State.WaitLobby:
                if (LobbyBehaviour.Instance != null && PlayerControl.LocalPlayer != null) SetState(State.Settle);
                else if (Elapsed > 30f) Finish("部屋に入れませんでした", true);
                break;

            case State.Settle:
                // 入りきってから次の人に番を回す
                if (Elapsed < SettleMargin) break;
                LobbyTurn.MarkJoined(Instance.Index);
                Finish("部屋に参加しました", false);
                break;
        }
    }
}
