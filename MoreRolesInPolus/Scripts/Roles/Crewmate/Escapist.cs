using MoreRolesInPolus.Scripts.Core;
using Virial.Achievements;

namespace MoreRolesInPolus.Roles.Crewmate;

/// <summary>
/// 人狼ジャッジメントの「逃亡者」。会議中に逃げ込み先を選び、次のタスク中はキルされない。
/// 逃げ込み先がインポスターなら追放のときに死に、逃げ込み先が追放されたら一緒に追放され、キルされたら一緒に死ぬ。
/// </summary>
public class Escapist
  : DefinedSingleAbilityRoleTemplate<Escapist.Ability>,
    DefinedRole,
    HasCitation,
    IAssignableDocument
{
  private Escapist()
    : base(
      "escapist",
      new(240, 150, 180),
      RoleCategory.CrewmateRole,
      NebulaTeams.CrewmateTeam,
      [
        NumOfEscapesOption,
        CanEscapeToSameTargetInARowOption,
        EscapingVisionOption,
        HideFromAdminAndVitalsOption,
      ]
    ) { }

  Image? DefinedAssignable.IconImage => iconImage;
  static readonly Image iconImage = NebulaAPI
    .AddonAsset.GetResource("Crewmate/Escapist/Escapist.png")!
    .AsImage()!;

  Citation? HasCitation.Citation => AddonCitations.JinroJudgement;

  private static readonly IntegerConfiguration NumOfEscapesOption =
    NebulaAPI.Configurations.Configuration(
      "options.role.escapist.numOfEscapes",
      (0, 10, 1),
      2,
      null,
      num => num == 0 ? Language.Translate("options.noLimit") : num.ToString()
    );
  private static readonly BoolConfiguration CanEscapeToSameTargetInARowOption =
    NebulaAPI.Configurations.Configuration("options.role.escapist.canEscapeToSameTargetInARow", false);
  private static readonly FloatConfiguration EscapingVisionOption =
    NebulaAPI.Configurations.Configuration(
      "options.role.escapist.escapingVision",
      (0.25f, 1f, 0.125f),
      0.75f,
      FloatConfigurationDecorator.Ratio
    );
  private static readonly BoolConfiguration HideFromAdminAndVitalsOption =
    NebulaAPI.Configurations.Configuration("options.role.escapist.hideFromAdminAndVitals", true);

  public static readonly Escapist MyRole = new();

  /// <summary>称号はプリプロセス中に登録する。MyRole を関係する役職に使うので、MyRole より後で初期化する</summary>
  private static readonly bool TitlesRegistered = RegisterTitles();

  private static bool RegisterTitles()
  {
    void Register(string key, TrophyLevel rank, bool isSecret = false) =>
      AddonTitles.Register(MyRole, "escapist", key, rank, isSecret);

    Register("common1", TrophyLevel.Silver);
    Register("common2", TrophyLevel.Silver);
    Register("common3", TrophyLevel.Gold);
    Register("another1", TrophyLevel.Silver, true);
    Register("another2", TrophyLevel.Silver, true);
    Register("challenge", TrophyLevel.Rainbow);
    return true;
  }

  private static string TitleId(string key) => AddonTitles.Id("escapist", key);

  bool IAssignableDocument.HasTips => true;
  bool IAssignableDocument.HasAbility => true;

  IEnumerable<AssignableDocumentImage> IAssignableDocument.GetDocumentImages()
  {
    yield return new(meetingSprite, "role.escapist.ability.escape");
  }

  /// <summary>会議中の投票欄に置くボタンの画像。独裁者と同じく、2 倍の解像度を 230 で読む</summary>
  static readonly Image meetingSprite = NebulaAPI
    .AddonAsset.GetResource("Crewmate/Escapist/escapistMeetingButton.png")!
    .AsImage(230f)!;

  // 専用の死因（ドクターのバイタルなどに出る）。タグはプリプロセス中に作る必要があり、役職を作る Roles フェイズで初期化される
  static readonly CommunicableTextTag CaughtState =
    NebulaAPI.Preprocessor!.RegisterCommunicableText("state.escapist.caught");
  static readonly CommunicableTextTag ExiledTogetherState =
    NebulaAPI.Preprocessor!.RegisterCommunicableText("state.escapist.exiledTogether");
  static readonly CommunicableTextTag KilledTogetherState =
    NebulaAPI.Preprocessor!.RegisterCommunicableText("state.escapist.killedTogether");

  /// <summary>逃げ込み先を選んでいないことを表す PlayerId</summary>
  private const byte NoTarget = byte.MaxValue;

  public override Ability CreateAbility(GamePlayer player, int[] arguments)
  {
    int max = NumOfEscapesOption.GetValue();
    return new(player, arguments.GetAsBool(0), arguments.Get(1, max == 0 ? 1000 : max));
  }

  [NebulaRPCHolder]
  public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
  {
    int[] IPlayerAbility.AbilityArguments => [IsUsurped.AsInt(), LeftUses];

    int LeftUses;

    /// <summary>今回の逃げ込み先。会議中に選ばれ、次の会議の開始で外れる。全員の端末で同じ値を持つ</summary>
    GamePlayer? EscapeTarget = null;

    /// <summary>前回の逃げ込み先。続けて同じ人に逃げ込めない設定のときに使う</summary>
    GamePlayer? LastEscapeTarget = null;

    /// <summary>
    /// 逃亡者がタスクのミニゲームを開いているか。キルの判定はホストで行うので、本人の端末から全員に知らせる
    /// </summary>
    bool IsDoingTask = false;

    /// <summary>1 試合で、逃げ込んで次の会議まで生き残った回数（渡り鳥）</summary>
    AchievementToken<int>? SurvivedEscapesToken;

    /// <summary>1 試合で、逃げ込んでいるあいだにキルを防いだ回数（逃げるは恥だが役に立つ）</summary>
    AchievementToken<int>? GuardedKillsToken;

    public Ability(GamePlayer player, bool isUsurped, int leftUses)
      : base(player, isUsurped)
    {
      LeftUses = leftUses;

      if (AmOwner && !isUsurped)
      {
        SurvivedEscapesToken = new(TitleId("common3"), 0, (count, _) => count >= 3);
        GuardedKillsToken = new(
          TitleId("challenge"),
          0,
          (count, _) =>
            count >= 2
            && !MyPlayer.IsDead
            && (NebulaGameManager.Instance?.EndState?.Winners.Test(MyPlayer) ?? false)
        );
      }
    }

    /// <summary>本人の端末で、条件を満たした称号を記録する</summary>
    void GainTitle(string key)
    {
      if (AmOwner && !IsUsurped)
        AddonTitles.Gain("escapist", key);
    }

    /// <summary>逃げ込みが効いているか（逃げ込み先を選び、追放を越えてタスク中にいる）</summary>
    bool IsEscaping =>
      EscapeTarget != null && !MyPlayer.IsDead && !MeetingHud.Instance && !ExileController.Instance;

    [NebulaRPC]
    public static void RpcEscape(GamePlayer caller, byte targetId)
    {
      if (!caller.TryGetAbility<Ability>(out var ability))
        return;
      ability.EscapeTarget = NebulaGameManager.Instance?.GetPlayer(targetId);
      ability.LeftUses--;
    }

    [NebulaRPC]
    public static void RpcSetDoingTask(GamePlayer caller, bool doingTask)
    {
      if (caller.TryGetAbility<Ability>(out var ability))
        ability.IsDoingTask = doingTask;
    }

    enum ExileFate
    {
      /// <summary>何も起きない（逃げ込んでいない、または逃亡者自身が追放された）</summary>
      None,
      /// <summary>逃げ込み先がインポスターだったので死ぬ</summary>
      KilledByImpostor,
      /// <summary>逃げ込み先が追放されたので、一緒に追放される</summary>
      ExiledWithTarget,
    }

    /// <summary>追放のときに逃亡者がどうなるか</summary>
    ExileFate GetExileFate(IEnumerable<GamePlayer> exiled)
    {
      if (EscapeTarget == null || MyPlayer.IsDead || exiled.Contains(MyPlayer))
        return ExileFate.None;
      if (exiled.Contains(EscapeTarget))
        return ExileFate.ExiledWithTarget;
      if (!EscapeTarget.IsDead && EscapeTarget.IsImpostor)
        return ExileFate.KilledByImpostor;
      return ExileFate.None;
    }

    void OnMeetingStart(MeetingStartEvent ev)
    {
      // 逃げ込み先が残ったまま会議を迎えた＝逃げ込んで生き残った
      if (EscapeTarget != null && !MyPlayer.IsDead)
      {
        GainTitle("common1");
        if (SurvivedEscapesToken != null)
          SurvivedEscapesToken.Value++;
      }

      LastEscapeTarget = EscapeTarget;
      EscapeTarget = null;
      IsDoingTask = false;

      if (AmOwner && LeftUses > 0 && !MyPlayer.IsDead)
        CreateMeetingButtons();
    }

    /// <summary>各プレイヤーの投票欄に置く、逃げ込み先を選ぶボタン</summary>
    readonly List<(GamePlayer Target, GameObject Button)> MeetingButtons = [];

    /// <summary>独裁者と同じく、議論中にも押せるよう CancelButton を複製して置く</summary>
    void CreateMeetingButtons()
    {
      MeetingButtons.Clear();
      foreach (var area in MeetingHud.Instance.playerStates)
      {
        var target = NebulaGameManager.Instance?.GetPlayer(area.PlayerId.Value);
        if (target == null || target.AmOwner)
          continue;

        var template = area.Buttons.transform.Find("CancelButton").gameObject;
        var obj = UnityEngine.Object.Instantiate(template, area.transform);
        obj.name = "EscapistButton";
        obj.transform.localPosition = MeetingHudExtension.VoteAreaPlayerIconPos;
        obj.GetComponent<SpriteRenderer>().sprite = meetingSprite.GetSprite();

        var button = obj.GetComponent<PassiveButton>();
        button.OnClick.RemoveAllListeners();
        button.OnClick.AddListener(() => OnClickMeetingButton(target));

        obj.SetActive(false);
        MeetingButtons.Add((target, obj));
      }
    }

    void OnClickMeetingButton(GamePlayer target)
    {
      if (!CanUseMeetingButtons() || !CanEscapeTo(target) || !MeetingHudExtension.CanInvokeSomeAction)
        return;
      if (IsUsurped)
      {
        LeftUses--;
        return;
      }
      RpcEscape(MyPlayer, target.PlayerId);
    }

    bool CanUseMeetingButtons() =>
      EscapeTarget == null
      && LeftUses > 0
      && !MyPlayer.IsDead
      && MeetingHud.Instance
      && MeetingHud.Instance.CurrentState
        is MeetingHud.MeetingStates.Discussion
          or MeetingHud.MeetingStates.NotVoted
          or MeetingHud.MeetingStates.Voted;

    [Local]
    void OnGameUpdate(GameUpdateEvent ev)
    {
      UpdateMeetingButtons();

      // タスクのミニゲームを開いているあいだは守られない。開け閉めしたらすぐ全員に知らせる
      bool doingTask =
        IsEscaping && Minigame.Instance && Minigame.Instance.MyTask != null;
      if (doingTask != IsDoingTask)
        RpcSetDoingTask(MyPlayer, doingTask);
    }

    void UpdateMeetingButtons()
    {
      if (MeetingButtons.Count == 0)
        return;
      if (!MeetingHud.Instance)
      {
        MeetingButtons.Clear();
        return;
      }

      bool canUse = CanUseMeetingButtons();
      foreach (var (target, button) in MeetingButtons)
      {
        if (button)
          button.SetActive(canUse && CanEscapeTo(target));
      }
    }

    bool CanEscapeTo(GamePlayer target) =>
      !target.IsDead && (CanEscapeToSameTargetInARowOption || target != LastEscapeTarget);

    /// <summary>全員の端末で呼ばれ、追放の結果に合わせて逃げ込みを確定させる（キルの判定をするホストとそろえるため）</summary>
    void OnMeetingPreSync(MeetingPreSyncEvent ev)
    {
      var exiled = MeetingHudExtension.ExiledAllModCache ?? [];
      var fate = GetExileFate(exiled);
      if (AmOwner && fate == ExileFate.KilledByImpostor)
        MyPlayer.VanillaPlayer.ModMarkAsExtraVictim(null, CaughtState, CaughtState);
      else if (AmOwner && fate == ExileFate.ExiledWithTarget)
      {
        MyPlayer.VanillaPlayer.ModMarkAsExtraVictim(null, ExiledTogetherState, ExiledTogetherState);
        GainTitle("another2");
      }

      // 逃亡者自身が追放されたときも含め、追放を越えた逃げ込みだけを残す
      if (fate != ExileFate.None || exiled.Contains(MyPlayer))
        EscapeTarget = null;
    }

    [Local]
    void OnTaskPhaseStart(TaskPhaseStartEvent ev)
    {
      // 家にいないので、アドミンとバイタルに映らない（次の会議で外れる）
      if (IsEscaping && HideFromAdminAndVitalsOption)
        MyPlayer.GainAttribute(PlayerAttributes.Isolation, 100000f, false, 0, "escapist");
    }

    [Local]
    void OnUpdateLightRange(LightRangeUpdateEvent ev)
    {
      if (IsEscaping)
        ev.LightRange *= EscapingVisionOption.GetValue();
    }

    [OnlyHost]
    void OnCheckKilled(PlayerCheckKilledEvent ev)
    {
      // 逃げ込んでいるあいだは、タスクのミニゲームを開いているときを除いてキルされない
      if (ev.Player != MyPlayer || ev.Killer == MyPlayer || ev.IsMeetingKill)
        return;
      if (IsEscaping && !IsDoingTask)
        ev.Result = KillResult.ObviousGuard;
    }

    [Local]
    void OnPlayerMurdered(PlayerMurderedEvent ev)
    {
      // 逃げ込み先がキルされたら、逃亡者も一緒に死ぬ
      if (!IsEscaping || ev.Dead != EscapeTarget)
        return;
      EscapeTarget = null;
      MyPlayer.Suicide(KilledTogetherState, KilledTogetherState, KillParameter.RemoteKill);
      GainTitle("another1");
    }

    [Local]
    void OnGuarded(PlayerGuardEvent ev)
    {
      // 逃げ込みでキルを防いだ（ホストの判定結果が全員に届く）
      if (ev.Player != MyPlayer || !IsEscaping)
        return;
      GainTitle("common2");
      if (GuardedKillsToken != null)
        GuardedKillsToken.Value++;
    }
  }
}
