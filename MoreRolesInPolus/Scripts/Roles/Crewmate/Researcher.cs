using MoreRolesInPolus.Scripts.Core;

namespace MoreRolesInPolus.Roles.Crewmate;

[NebulaRPCHolder]
public class Researcher
  : DefinedSingleAbilityRoleTemplate<Researcher.Ability>,
    DefinedRole,
    IAssignableDocument
{
  private Researcher()
    : base(
      "researcher",
      new(104, 251, 194),
      RoleCategory.CrewmateRole,
      NebulaTeams.CrewmateTeam,
      [SurveyCooldownOption, SurveyDurationOption, SurveyTimeOption, MaxSurveyOption]
    ) { }

  Image? DefinedAssignable.IconImage => iconImage;
  static readonly Image iconImage = NebulaAPI
    .AddonAsset.GetResource(string.Format("Crewmate/Researcher/Researcher.png"))!
    .AsImage()!;

  // 設定オプション
  private static readonly FloatConfiguration SurveyCooldownOption =
    NebulaAPI.Configurations.Configuration(
      "options.role.researcher.surveyCooldown",
      (0f, 60f, 2.5f),
      20f,
      FloatConfigurationDecorator.Second
    );
  private static readonly FloatConfiguration SurveyDurationOption =
    NebulaAPI.Configurations.Configuration(
      "options.role.researcher.surveyDuration",
      (1f, 10f, 0.5f),
      3f,
      FloatConfigurationDecorator.Second
    );
  private static readonly FloatConfiguration SurveyTimeOption =
    NebulaAPI.Configurations.Configuration(
      "options.role.researcher.surveytime",
      (10f, 60f, 2.5f),
      30f,
      FloatConfigurationDecorator.Second
    );
  private static readonly IntegerConfiguration MaxSurveyOption =
    NebulaAPI.Configurations.Configuration(
      "options.role.researcher.maxsurvey",
      (0, 10, 1),
      5,
      null,
      num => num == 0 ? Language.Translate("options.noLimit") : num.ToString()
    );

  public static readonly Researcher MyRole = new();

  // 称号（MyRole を関係する役職に使うので、MyRole より後で登録する）
  private static readonly bool TitlesRegistered = RegisterTitles();

  private static bool RegisterTitles()
  {
    AddonTitles.Register(MyRole, "researcher", "common1", Virial.Achievements.TrophyLevel.Silver);
    AddonTitles.Register(MyRole, "researcher", "common2", Virial.Achievements.TrophyLevel.Silver);
    AddonTitles.Register(MyRole, "researcher", "common3", Virial.Achievements.TrophyLevel.Gold);
    AddonTitles.Register(MyRole, "researcher", "challenge", Virial.Achievements.TrophyLevel.Rainbow);
    return true;
  }

  bool IAssignableDocument.HasTips => true;
  bool IAssignableDocument.HasAbility => true;

  IEnumerable<AssignableDocumentImage> IAssignableDocument.GetDocumentImages()
  {
    yield return new(researchSprite, "role.researcher.ability.survey");
    yield return new(trackSprite, "role.researcher.ability.track");
  }

  IEnumerable<AssignableDocumentReplacement> IAssignableDocument.GetDocumentReplacements()
  {
    yield return IAssignableDocument.GetKeyInput("%KEY%", VirtualKeyInput.AidAction);
    yield return new("%SEC%", SurveyDurationOption.GetValue().ToString("F1"));
    yield return new("%LOGSEC%", SurveyTimeOption.GetValue().ToString("F1"));
  }

  // 通常モード用の画像
  private static readonly Image researchSprite = NebulaAPI
    .AddonAsset.GetResource(string.Format("Crewmate/Researcher/ResearchButton.png"))!
    .AsImage(115f)!;

  // 監視モード用の画像
  private static readonly Image trackSprite = NebulaAPI
    .AddonAsset.GetResource("Crewmate/Researcher/TrackButton.png")!
    .AsImage(115f)!;

  public override Ability CreateAbility(GamePlayer player, int[] arguments)
  {
    int maxSurveyValue = MaxSurveyOption.GetValue();
    int defaultUses = maxSurveyValue == 0 ? 1000 : maxSurveyValue;
    return new(player, arguments.GetAsBool(0), arguments.Get(1, defaultUses));
  }

  public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
  {
    int[] IPlayerAbility.AbilityArguments => [IsUsurped.AsInt(), leftUses];

    private record ActionHistory(float Time, GamePlayer Player, string Text);

    private List<ActionHistory> allActions = [];

    private Dictionary<byte, string> lastPlayerRooms = new();
    private float timer = 0f;
    private const float interval = 0.25f;

    int leftUses;

    // 履歴レコード: 時間、プレイヤー、内容、そしてモードプレフィックス(H/T)
    private record SurveyHistory(float Time, GamePlayer Player, string Content, string Prefix);

    private List<SurveyHistory> InthisturnResult = [];

    // 称号の判定に使う
    AchievementToken<int>? usesToken; // 現場百遍: 1 試合で使った回数
    readonly HashSet<byte> examinedPlayers = []; // 1 試合で調査か追跡をした相手
    readonly HashSet<byte> trackedThisTurn = []; // このターンに追跡した相手
    int usesThisTurn = 0;
    GamePlayer? myVoteFor = null; // この会議で自分が投票した相手

    // 監視モード: 会議まで行動を記録する
    private record LoggerTarget(float StartTime, GamePlayer Player);

    private List<LoggerTarget> activeLoggers = [];

    private enum AbilityMode
    {
      Instant, // 直前の行動を確認
      Logger, // 会議開始まで監視
    }

    private AbilityMode currentMode = AbilityMode.Instant;

    string GetHistory(GamePlayer player, float from, float to)
    {
      StringBuilder result = new();
      byte targetPlayerId = player.PlayerId;

      foreach (var action in allActions)
      {
        if (action.Player.PlayerId != targetPlayerId)
        {
          continue;
        }

        if (action.Time < from)
        {
          continue;
        }

        if (action.Time >= to)
        {
          break;
        }
        float elapsedTime = to - action.Time;
        int roundedElapsedTime = (int)MathF.Round(elapsedTime, MidpointRounding.AwayFromZero);
        result.AppendLine($"{roundedElapsedTime}秒前に" + action.Text);
      }
      return result.ToString();
    }

    public Ability(GamePlayer player, bool isUsurped, int leftUses)
      : base(player, isUsurped)
    {
      if (leftUses < 0)
      {
        int MaxSurveyValue = MaxSurveyOption.GetValue();
        this.leftUses = MaxSurveyValue == 0 ? 1000 : MaxSurveyValue;
      }
      else
      {
        this.leftUses = leftUses;
      }

      if (AmOwner)
      {
        if (!isUsurped)
          usesToken = new(AddonTitles.Id("researcher", "common1"), 0, (count, _) => count >= 5);

        //実行対象をきめるやつ
        var surveyTracker = ObjectTrackers.ForPlayerlike(
          this,
          null,
          MyPlayer,
          (p) =>
            ObjectTrackers.PlayerlikeStandardPredicate(p)
            && !activeLoggers.Any(l => l.Player.PlayerId == p.RealPlayer.PlayerId)
            && !InthisturnResult.Any(h => h.Player.PlayerId == p.RealPlayer.PlayerId)
        );

        //発火ボタン
        var surveyButton = NebulaAPI.Modules.EffectButton(
          this,
          MyPlayer,
          VirtualKeyInput.Ability,
          SurveyCooldownOption,
          SurveyDurationOption,
          "survey",
          researchSprite,
          _ => surveyTracker.CurrentTarget != null,
          _ => this.leftUses > 0
        );
        if (this.leftUses < 20)
          surveyButton.ShowUsesIcon(4, this.leftUses.ToString());

        // Shiftキーでモードを切り替える
        surveyButton.BindSubKey(VirtualKeyInput.AidAction, "researcher.switch", true);

        ButtonEffect.SetAidAction(
          surveyButton,
          this,
          this,
          MyPlayer,
          () =>
          {
            if (surveyButton.IsInEffect)
              return;

            // モード切替
            currentMode =
              currentMode == AbilityMode.Instant ? AbilityMode.Logger : AbilityMode.Instant;

            // ボタンの見た目を更新
            if (currentMode == AbilityMode.Logger)
            {
              surveyButton.SetImage(trackSprite);
              surveyButton.SetLabel("researcher.track");
            }
            else
            {
              surveyButton.SetImage(researchSprite);
              surveyButton.SetLabel("researcher.survey");
            }
          }
        );

        //調査を実行する関数
        void examinePlayer()
        {
          var examineTime = Time.time;
          var TargetPlayer = surveyTracker.CurrentTarget.RealPlayer;

          if (currentMode == AbilityMode.Instant)
          {
            // 通常: 直前の行動を取得 (History)
            string historyText = GetHistory(
              TargetPlayer,
              examineTime - SurveyTimeOption.GetValue(),
              examineTime
            );
            InthisturnResult.Add(new(examineTime, TargetPlayer, historyText, "調査モード"));
          }
          else
          {
            // 監視: ターゲットを登録 (Track)
            activeLoggers.Add(new LoggerTarget(examineTime, TargetPlayer));
          }

          this.leftUses--;

          examinedPlayers.Add(TargetPlayer.PlayerId);
          if (currentMode == AbilityMode.Logger)
            trackedThisTurn.Add(TargetPlayer.PlayerId);
          usesThisTurn++;
          if (usesToken != null)
            usesToken.Value++;
          surveyButton.UpdateUsesIcon(this.leftUses.ToString());
        }

        //近くにいないとだめだよ
        surveyButton.OnEffectStart = _ => surveyTracker.KeepAsLongAsPossible = true;
        surveyButton.OnEffectEnd = (button) =>
        {
          surveyTracker.KeepAsLongAsPossible = false;
          if (surveyTracker.CurrentTarget == null)
            return;
          if (MeetingHud.Instance)
            return;

          if (
            GameOperatorManager
              .Instance?.Run(
                new PlayerInteractPlayerLocalEvent(
                  MyPlayer,
                  surveyTracker.CurrentTarget,
                  new(RealPlayerOnly: true)
                )
              )
              .IsCanceled ?? true
          )
            return;

          if (!button.EffectTimer!.IsProgressing)
            examinePlayer();

          surveyButton.StartCoolDown();
        };

        surveyButton.OnUpdate = (button) =>
        {
          if (!button.IsInEffect)
            return;
          if (surveyTracker.CurrentTarget == null)
            button.InterruptEffect();
        };

        surveyButton.EffectTimer = NebulaAPI.Modules.Timer(this, SurveyDurationOption);
        surveyButton.SetLabel("researcher.survey");
        surveyButton.SetAsUsurpableButton(this);
      }
    }

    // どこの部屋に"入った"か
    [Local]
    public void OnUpdate(GameUpdateEvent ev)
    {
      // マップが完全に読み込まれているかチェック
      if (NebulaAPI.CurrentGame?.CurrentMap == null)
        return;

      timer += ev.DeltaTime;
      if (timer < interval)
        return;
      timer = 0f;

      foreach (var gplayer in GamePlayer.AllPlayers)
      {
        if (gplayer.IsDead || gplayer.IsBlown)
          continue;

        Virial.Compat.Vector2 pos = gplayer.TruePosition;

        var roomResult = NebulaAPI.CurrentGame.CurrentMap.GetRoomName(pos, false, false, false);

        byte id = gplayer.PlayerId;

        if (!lastPlayerRooms.ContainsKey(id))
        {
          lastPlayerRooms[id] = "";
        }

        if (lastPlayerRooms[id] != roomResult)
        {
          allActions.Add(new(Time.time, gplayer, $"{roomResult}に入ったようだ..."));
          lastPlayerRooms[id] = roomResult;
        }
      }
    }

    //ベントに入った
    [Local]
    void OnEnterVent(PlayerVentEnterEvent ev)
    {
      allActions.Add(new(Time.time, ev.Player, "ベントに入ったようだ..."));
    }

    //アクションを起こした
    [Local]
    void OnDoGameAction(PlayerDoGameActionEvent ev)
    {
      if (ev.ActionType.IsPlacementAction == true)
      {
        allActions.Add(new(Time.time, ev.Player, "何かを設置したようだ..."));
      }
      else if (ev.ActionType.IsPhysicalAction == true)
      {
        allActions.Add(new(Time.time, ev.Player, "状態を変化させたようだ..."));
      }
      else
      {
        allActions.Add(new(Time.time, ev.Player, "アクションを起こした！"));
      }
    }

    //殺害された
    [Local]
    void OnPlayerMurdered(PlayerKillPlayerEvent ev)
    {
      allActions.Add(new(Time.time, ev.Dead, "何らかの手段によって殺害された"));
    }

    //ドアを開けた
    [Local]
    void OnOpenDoor(Virial.Events.Game.Door.PlayerOpenDoorEvent ev)
    {
      // ドアは部屋の境目にあり、位置から部屋を調べると「部屋外」になるので、ドア自身が持つ部屋を使う
      var room = AmongUsUtil.ToDisplayString(ev.Door.VanillaObject.Room);
      allActions.Add(new(Time.time, ev.Player, string.IsNullOrEmpty(room) ? "どこかのドアを開けたようだ..." : $"{room}のドアを開けたようだ..."));
    }

    void GainTitle(string key)
    {
      if (AmOwner && !IsUsurped)
        AddonTitles.Gain("researcher", key);
    }

    [Local]
    void OnCastVote(PlayerVoteCastLocalEvent ev)
    {
      if (ev.Voter == MyPlayer)
        myVoteFor = ev.VoteFor;
    }

    [Local]
    void OnExiled(PlayerExiledEvent ev)
    {
      var exiled = ev.Player;

      // 張り込み: 追跡したインポスターが、その次の会議で追放された
      if (exiled.IsImpostor && trackedThisTurn.Contains(exiled.PlayerId))
        GainTitle("common2");

      // 動かぬ証拠: 調査か追跡をしたインポスターに投票して追放した
      if (exiled.IsImpostor && examinedPlayers.Contains(exiled.PlayerId) && myVoteFor == exiled)
        GainTitle("common3");

      // 深淵に飲み込まれし者の行方は: 1 ターンで 3 回使い、次の会議で自分が追放された（使える回数が 5 回以上のときだけ）
      int maxSurvey = MaxSurveyOption.GetValue();
      if (exiled == MyPlayer && usesThisTurn >= 3 && (maxSurvey == 0 || maxSurvey >= 5))
        GainTitle("challenge");
    }

    [Local]
    void OnTaskPhaseStart(TaskPhaseStartEvent ev)
    {
      trackedThisTurn.Clear();
      usesThisTurn = 0;
    }

    //ミーティングボタンが押されたときに実行する
    //検証
    [Local]
    void OnMeetingStart(MeetingStartEvent ev)
    {
      myVoteFor = null;

      // 監視中のログを処理する
      foreach (var logger in activeLoggers)
      {
        var now = Time.time;
        string historyText = GetHistory(logger.Player, logger.StartTime, now);
        // Timeには監視開始時刻を入れる。プレフィックスは "T" (Track)
        InthisturnResult.Add(new(logger.StartTime, logger.Player, historyText, "追跡モード"));
      }
      activeLoggers.Clear();

      foreach (var history in InthisturnResult)
      {
        var now = Time.time;
        // 経過時間を計算 (監視モードなら開始からの時間)
        float elapsedTime = now - history.Time;
        int roundedElapsedTime = (int)MathF.Round(elapsedTime, MidpointRounding.AwayFromZero);

        var textContent = history.Content == "" ? "何もしていないようだ。" : history.Content;

        var playerinfo = NebulaAPI.GUI.VerticalHolder(
          Virial.Media.GUIAlignment.Left,
          NebulaAPI.GUI.RawText(
            Virial.Media.GUIAlignment.Left,
            NebulaAPI.GUI.GetAttribute(Virial.Text.AttributeAsset.OverlayTitle),
            textContent
          )
        );
        float cachedX = 0f;
        float cachedY = 0f;

        var Holder = NebulaAPI.GUI.VerticalHolder(
          GUIAlignment.Left,
          new NoSGUIText(
            GUIAlignment.Left,
            AttributeAsset.OverlayTitle,
            NebulaAPI.GUI.RawTextComponent(history.Player.PlayerName)
          )
          {
            PostBuilder = (textObj) =>
            {
              cachedX = textObj.rectTransform.sizeDelta.x;
              cachedY = textObj.rectTransform.sizeDelta.y;
            },
          },
          new NoSGameObjectGUIWrapper(GUIAlignment.Left, () => (null!, new(0f, -cachedY))),
          NebulaAPI.GUI.HorizontalHolder(
            GUIAlignment.Right,
            NebulaAPI.GUI.HorizontalMargin(cachedX),
            new NoSGUIText(
              GUIAlignment.Right,
              AttributeAsset.OverlayTitle,
              NebulaAPI.GUI.RawTextComponent($"{history.Prefix} {roundedElapsedTime}秒前")
            )
          ),
          playerinfo
        );

        NebulaAPI
          .CurrentGame?.GetModule<MeetingOverlayHolder>()
          ?.RegisterOverlay(
            Holder,
            MeetingOverlayHolder.IconsSprite[6],
            (MyRole as DefinedRole).Color
          );
      }

      InthisturnResult.Clear();
      allActions.Clear();
    }

    // タスク中にプレイヤーの頭上にステータスを表示する
    [Local]
    void ReflectRoleName(PlayerSetFakeRoleNameEvent ev)
    {
      if (ev.InMeeting)
        return;

      // 優先順位: 監視中 > 調査済み

      if (activeLoggers.Any(l => l.Player.PlayerId == ev.Player.PlayerId))
      {
        ev.Alternate("調査中");
        return;
      }

      if (InthisturnResult.Any(h => h.Player.PlayerId == ev.Player.PlayerId))
      {
        ev.Alternate("調査完了");
        return;
      }
    }
  }
}
