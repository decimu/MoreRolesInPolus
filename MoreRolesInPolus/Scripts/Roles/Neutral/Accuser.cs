using Nebula.Roles.Complex;
using Virial.Runtime;
namespace MoreRolesInPolus.Roles.Neutral;

[NebulaPreprocess(PreprocessPhase.BuildAssignmentTypes)]
internal class AccuserTeamInfo
{
    static public RoleTeam? MyTeam { get; private set; }
    static public GameEnd? End { get; private set; }
    static public Virial.Color TeamColor { get; private set; }
    static private void Preprocess(NebulaPreprocessor preprocessor)
    {
        TeamColor = new(120, 60, 180);
        MyTeam = preprocessor.CreateTeam("teams.accuser", TeamColor, TeamRevealType.OnlyMe);
        End = preprocessor.CreateEnd("accuser", TeamColor);
    }
}

// Accuser（告発者）
// 推測を成功させて勝利を目指す第三陣営役職
// 会議中以外暇な役職になってしまってるから仕事を与えたい
// 今考えてるのとしては何かをしないとゲッサーの玉をゲットできないというもの
// 例えば生きてる人全員に一回触れる(クールタイムなどはなしただ触れるだけもしくはクリックするだけ)
internal class Accuser : DefinedRoleTemplate, DefinedRole
{
    // 何回推測成功したら勝ちか
    static private IntegerConfiguration NumOfGuessToWinOption = NebulaAPI.Configurations.Configuration("options.role.accuser.NumOfGuessToWinOption", (1, 10), 2);
    // 一回の会議で何回推測できるか
    static private IntegerConfiguration NumOfGuessPerMeetingOption = NebulaAPI.Configurations.Configuration("options.role.accuser.numOfGuessPerMeeting", (1, 10), 1);
    //許容ミス回数
    static private IntegerConfiguration NumOfMissAllowedOption = NebulaAPI.Configurations.Configuration("options.role.accuser.numOfMissAllowed", (0, 20), 1);
    //ミスしたときにその会議中は推測できなくするか
    static private BoolConfiguration DisableGuessAfterMissOption = NebulaAPI.Configurations.Configuration("options.role.accuser.disableGuessAfterMiss", false);
    static public Accuser MyRole = new Accuser();
    // 統計：推測した回数
    static private GameStatsEntry StatsGuess = NebulaAPI.CreateStatsEntry("stats.accuser.guess", GameStatsCategory.Roles, MyRole);

    private Accuser() : base("accuser", AccuserTeamInfo.TeamColor, RoleCategory.NeutralRole, AccuserTeamInfo.MyTeam!, [NumOfGuessToWinOption, NumOfGuessPerMeetingOption, NumOfMissAllowedOption, DisableGuessAfterMissOption])
    {
    }

    Image? DefinedAssignable.IconImage => (Guesser.MyEvilRole as DefinedRole).IconImage;
    static private readonly Image researchSprite = NebulaAPI.AddonAsset.GetResource(string.Format("Crewmate/Researcher/ResearchButton.png"))!.AsImage(115f)!;
    RuntimeRole RuntimeAssignableGenerator<RuntimeRole>.CreateInstance(GamePlayer player, int[] arguments) => new Instance(player, arguments);

    public class Instance : RuntimeAssignableTemplate, RuntimeRole
    {
        DefinedRole RuntimeRole.Role => MyRole;
        // ゲーム全体で残っている推測回数
        private int leftGuess;
        // 勝利に必要な推測成功回数（初期値)
        private int totalGuesses;
        //ミス許容回数
        private int leftMiss;
        //この会議でミスしたか
        private bool missedThisMeeting = false;
        // 推測ウィンドウの参照
        private MetaScreen? lastGuesserWindow = null;

        public Instance(GamePlayer player, int[] arguments) : base(player)
        {
            totalGuesses = arguments.Length >= 1 ? arguments[0] : NumOfGuessToWinOption;
            leftGuess = totalGuesses;
            leftMiss = arguments.Length >= 2 ? arguments[1] : NumOfMissAllowedOption;
        }

        public Instance(GamePlayer myPlayer) : base(myPlayer)
        {
            totalGuesses = NumOfGuessToWinOption;
            leftGuess = totalGuesses;
            leftMiss = NumOfMissAllowedOption;
        }

        // 
        int[]? RuntimeAssignable.RoleArguments => new int[] { totalGuesses, leftMiss};

        // 会議開始時：ゲッサーの能力を付与
        [Local]
        void OnMeetingStart(MeetingStartEvent ev)
        {
            missedThisMeeting = false;
            // この会議で残っている推測回数
            int leftGuessPerMeeting = NumOfGuessPerMeetingOption;

            // 各プレイヤーに推測ボタンを追加
            
            NebulaAPI.CurrentGame?.GetModule<MeetingPlayerButtonManager>()?.RegisterMeetingAction(
                new(MeetingPlayerButtonManager.Icons.AsLoader(0),
                state => {
                    var p = state.MyPlayer;
                    // 推測ウィンドウを開く
                    lastGuesserWindow = OpenGuessWindow(leftGuessPerMeeting, leftGuess, leftMiss, (r) =>
                    {
                        
                        if (PlayerControl.LocalPlayer.Data.IsDead) return;
                        if (!(MeetingHud.Instance.state == MeetingHud.MeetingStates.Voted || MeetingHud.Instance.state == MeetingHud.MeetingStates.NotVoted)) return;
                        if (!MeetingHudExtension.CanUseAbilityForLocal(p, true)) return;

                        // 統計：推測回数を記録
                        StatsGuess.Progress();
                        // 推測が正しいかチェック
                        bool isCorrect = p.Role.CheckGuessAbility(r);

                        if (isCorrect)
                        {
                            if (!missedThisMeeting)
                            {

                                NebulaAPI.CurrentGame?.LocalPlayer.MurderPlayer(p, PlayerState.Guessed, EventDetail.Guess, KillParameter.MeetingKill, KillCondition.BothAlive);
                                leftGuess--;
                                leftGuessPerMeeting--;
                            }
                        }
                            else
                        {

                            if(leftMiss <= 0) 
                            {
                                NebulaAPI.CurrentGame?.LocalPlayer.MurderPlayer(NebulaAPI.CurrentGame.LocalPlayer, PlayerState.Misguessed, EventDetail.Missed, KillParameter.MeetingKill, KillCondition.BothAlive);
                            }
                            else
                            {
                                leftMiss--;
                                if (DisableGuessAfterMissOption)
                                {
                                    missedThisMeeting = true;
                                }
                            }
                        }
                        // ウィンドウを閉じる
                        if (lastGuesserWindow) lastGuesserWindow!.CloseScreen();
                        lastGuesserWindow = null;
                    });
                },
                // ボタンを表示する条件
                p => !p.MyPlayer.IsDead && !p.MyPlayer.AmOwner && leftGuess > 0 && leftGuessPerMeeting > 0 && !missedThisMeeting && !PlayerControl.LocalPlayer.Data.IsDead && GameOperatorManager.Instance!.Run(new PlayerCanGuessPlayerLocalEvent(NebulaAPI.CurrentGame!.LocalPlayer, p.MyPlayer, true)).CanGuess
            ));
        }

        // 推測ウィンドウを開く
        private MetaScreen OpenGuessWindow(int leftGuessPerMeeting, int leftGuess,int leftMiss, Action<DefinedRole> onSelected)
        {
            // 会議ごとの制限がある場合は "1 (3)" のように表示
            string leftStr = leftGuessPerMeeting < leftGuess
                ? $"{leftGuessPerMeeting} ({leftGuess})"
                : leftGuess.ToString();

            // 残りミス回数を表示文字列に追加
            string header = Language.Translate("role.guesser.leftGuess") + " : " + leftStr
                          + "  " + Language.Translate("role.accuser.leftMiss") + " : " + leftMiss;

            return MeetingRoleSelectWindow.OpenRoleSelectWindow(null, r => r.CanBeGuess, GamePlayer.LocalPlayer?.FeelBeTrueCrewmate ?? false, header, onSelected);
        }

        // 自分が死亡した時：ウィンドウを閉じる
        [Local, OnlyMyPlayer]
        void OnDead(PlayerDieEvent ev)
        {
            if (lastGuesserWindow) lastGuesserWindow!.CloseScreen();
            lastGuesserWindow = null;
        }

        // プレイヤーが殺害された時：勝利条件をチェック
        [Local, OnlyMyPlayer]
        void OnGuessPlayer(PlayerKillPlayerEvent ev)
        {
            // 推測成功による殺害の場合
            if (ev.Dead.PlayerState == PlayerStates.Guessed)
            {
                // 必要な推測成功回数に達したら勝利
                if (leftGuess <= 0)
                {
                    var bitmask = BitMasks.AsPlayer();
                    bitmask.Add(MyPlayer);

                    NebulaAPI.CurrentGame?.RequestGameEnd(AccuserTeamInfo.End!, bitmask);
                }
            }
        }

        public void OnActivated()
        {
        }
    }
}