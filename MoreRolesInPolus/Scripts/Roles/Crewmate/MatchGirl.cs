using MoreRolesInPolus.Scripts.Core;

namespace MoreRolesInPolus.Roles.Crewmate;

/// <summary>
/// 人狼ジャッジメントの「マッチ売り」。会議中にもタスク中にもマッチを灯せる。
/// 灯した相手がインポスターなら、次の会議までそのインポスターのキルはすべて失敗し、本人には幻が見える。
/// マッチを使い切ると凍死する。
/// </summary>
public class MatchGirl
  : DefinedSingleAbilityRoleTemplate<MatchGirl.Ability>,
    DefinedRole,
    HasCitation,
    IAssignableDocument
{
  private MatchGirl()
    : base(
      "matchGirl",
      new(200, 170, 230),
      RoleCategory.CrewmateRole,
      NebulaTeams.CrewmateTeam,
      [NumOfMatchesOption, LightCooldownOption, TasksPerMatchOption, MaxMatchesOption, GlowSizeOption]
    ) { }

  Image? DefinedAssignable.IconImage => iconImage;
  static readonly Image iconImage = NebulaAPI
    .AddonAsset.GetResource("Crewmate/MatchGirl/MatchGirl.png")!
    .AsImage()!;

  Citation? HasCitation.Citation => AddonCitations.JinroJudgement;

  private static readonly IntegerConfiguration NumOfMatchesOption =
    NebulaAPI.Configurations.Configuration("options.role.matchGirl.numOfMatches", (1, 10), 3);
  private static readonly FloatConfiguration LightCooldownOption =
    NebulaAPI.Configurations.Configuration(
      "options.role.matchGirl.lightCooldown",
      (0f, 60f, 2.5f),
      20f,
      FloatConfigurationDecorator.Second
    );
  private static readonly IntegerConfiguration TasksPerMatchOption =
    NebulaAPI.Configurations.Configuration(
      "options.role.matchGirl.tasksPerMatch",
      (0, 10, 1),
      3,
      null,
      num => num == 0 ? Language.Translate("options.role.matchGirl.noRefill") : num.ToString()
    );
  private static readonly IntegerConfiguration MaxMatchesOption =
    NebulaAPI.Configurations.Configuration("options.role.matchGirl.maxMatches", (1, 15), 5);
  private static readonly FloatConfiguration GlowSizeOption =
    NebulaAPI.Configurations.Configuration(
      "options.role.matchGirl.glowSize",
      (0.25f, 2f, 0.25f),
      1f,
      FloatConfigurationDecorator.Ratio
    );

  /// <summary>マッチを使い切ったときの死因（ドクターのバイタルなどに出る）</summary>
  static readonly CommunicableTextTag FrozenState =
    NebulaAPI.Preprocessor!.RegisterCommunicableText("state.matchGirl.frozen");

  public static readonly MatchGirl MyRole = new();

  // 称号（MyRole を関係する役職に使うので、MyRole より後で登録する）
  private static readonly bool TitlesRegistered = RegisterTitles();

  private static bool RegisterTitles()
  {
    AddonTitles.Register(MyRole, "matchGirl", "common1", Virial.Achievements.TrophyLevel.Silver);
    AddonTitles.Register(MyRole, "matchGirl", "common2", Virial.Achievements.TrophyLevel.Silver);
    AddonTitles.Register(MyRole, "matchGirl", "common3", Virial.Achievements.TrophyLevel.Gold);
    AddonTitles.Register(MyRole, "matchGirl", "another1", Virial.Achievements.TrophyLevel.Silver, true);
    AddonTitles.Register(MyRole, "matchGirl", "another2", Virial.Achievements.TrophyLevel.Silver, true);
    AddonTitles.Register(MyRole, "matchGirl", "challenge", Virial.Achievements.TrophyLevel.Rainbow);
    return true;
  }

  bool IAssignableDocument.HasTips => true;
  bool IAssignableDocument.HasAbility => true;

  IEnumerable<AssignableDocumentImage> IAssignableDocument.GetDocumentImages()
  {
    yield return new(meetingSprite, "role.matchGirl.ability.meeting");
    yield return new(buttonSprite, "role.matchGirl.ability.task");
  }

  static readonly Image buttonSprite = NebulaAPI
    .AddonAsset.GetResource("Crewmate/MatchGirl/matchGirlButton.png")!
    .AsImage(115f)!;

  /// <summary>会議中の投票欄に置くボタンの画像。独裁者と同じく、2 倍の解像度を 230 で読む</summary>
  static readonly Image meetingSprite = NebulaAPI
    .AddonAsset.GetResource("Crewmate/MatchGirl/matchGirlMeetingButton.png")!
    .AsImage(230f)!;

  public override Ability CreateAbility(GamePlayer player, int[] arguments) =>
    new(player, arguments.GetAsBool(0), arguments.Get(1, NumOfMatchesOption.GetValue()));

  [NebulaRPCHolder]
  public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
  {
    int[] IPlayerAbility.AbilityArguments => [IsUsurped.AsInt(), Matches];

    int Matches;

    /// <summary>このターンにマッチを灯した相手。全員の端末で同じ。次の会議の開始で空になる</summary>
    readonly HashSet<byte> LitPlayers = [];

    /// <summary>最後にマッチを灯した相手。キルされた死体が通報されたときに知らせる</summary>
    GamePlayer? LastLitTarget = null;

    /// <summary>会議中にマッチを使い切ったので、追放のときに凍死する</summary>
    bool FreezesOnExile = false;

    /// <summary>前回マッチを補充してから終えたタスクの数</summary>
    int TasksSinceRefill = 0;

    ModAbilityButton? LightButton;

    /// <summary>停電中に、マッチを灯された人の周りに置く光</summary>
    readonly Dictionary<byte, SpriteRenderer> Glows = [];

    /// <summary>燐寸の魔女: 1 試合でマッチでキルを防いだ回数</summary>
    AchievementToken<int>? GuardedKillsToken;

    /// <summary>大晦日の奇跡: 凍死せずに生き残り、2 回以上キルを防いでクルーとして勝つ</summary>
    AchievementToken<int>? ChallengeToken;

    void GainTitle(string key)
    {
      if (AmOwner && !IsUsurped)
        AddonTitles.Gain("matchGirl", key);
    }

    public Ability(GamePlayer player, bool isUsurped, int matches)
      : base(player, isUsurped)
    {
      Matches = matches;

      if (!AmOwner)
        return;

      if (!isUsurped)
      {
        GuardedKillsToken = new(AddonTitles.Id("matchGirl", "common3"), 0, (count, _) => count >= 3);
        ChallengeToken = new(
          AddonTitles.Id("matchGirl", "challenge"),
          0,
          (count, _) =>
            count >= 2
            && !MyPlayer.IsDead
            && (NebulaGameManager.Instance?.EndState?.Winners.Test(MyPlayer) ?? false)
        );
      }

      var tracker = ObjectTrackers.ForPlayerlike(
        this,
        null,
        MyPlayer,
        p => ObjectTrackers.PlayerlikeStandardPredicate(p) && !LitPlayers.Contains(p.RealPlayer.PlayerId)
      );
      LightButton = NebulaAPI.Modules.AbilityButton(
        this,
        MyPlayer,
        VirtualKeyInput.Ability,
        LightCooldownOption.GetValue(),
        "matchGirl",
        buttonSprite,
        _ => tracker.CurrentTarget != null && MyPlayer.CanMove && Matches > 0,
        _ => !MyPlayer.IsDead
      );
      LightButton.ShowUsesIcon(3, Matches.ToString());
      LightButton.OnClick = button =>
      {
        var target = tracker.CurrentTarget?.RealPlayer;
        if (target == null)
          return;
        LightMatch(target);
        button.StartCoolDown();
      };
      LightButton.SetAsUsurpableButton(this);
    }

    [NebulaRPC]
    public static void RpcLight(GamePlayer caller, byte targetId)
    {
      if (!caller.TryGetAbility<Ability>(out var ability))
        return;
      ability.LitPlayers.Add(targetId);
      ability.LastLitTarget = NebulaGameManager.Instance?.GetPlayer(targetId);
    }

    /// <summary>本人の端末で呼ぶ。マッチを1本使い、使い切ったら凍死する</summary>
    void LightMatch(GamePlayer target)
    {
      if (Matches <= 0 || MyPlayer.IsDead)
        return;
      Matches--;
      LightButton?.UpdateUsesIcon(Matches.ToString());

      if (!IsUsurped)
        RpcLight(MyPlayer, target.PlayerId);

      if (Matches > 0)
        return;
      // おばあさんのもとへ
      GainTitle("another1");
      // 会議中に使い切ったら追放のとき、タスク中ならその場で凍死する
      if (MeetingHud.Instance)
        FreezesOnExile = true;
      else
        MyPlayer.Suicide(FrozenState, FrozenState, KillParameter.RemoteKill);
    }

    void OnMeetingStart(MeetingStartEvent ev)
    {
      LitPlayers.Clear();
      FreezesOnExile = false;
      ClearGlows();

      // 投票欄のボタンは NoS の仕組みに載せる。ゲッサーなどほかの役職のボタンと同じ場所を共有し、右クリックで切り替えられる
      if (AmOwner && Matches > 0 && !MyPlayer.IsDead)
        NebulaAPI
          .CurrentGame?.GetModule<MeetingPlayerButtonManager>()
          ?.RegisterMeetingAction(
            new(meetingSprite, state => LightMatch(state.MyPlayer), state => CanLightInMeeting(state.MyPlayer))
          );
    }

    bool CanLight(GamePlayer target) => !target.IsDead && !LitPlayers.Contains(target.PlayerId);

    bool CanLightInMeeting(GamePlayer target) =>
      Matches > 0 && !MyPlayer.IsDead && !target.AmOwner && CanLight(target);

    void OnGameUpdate(GameUpdateEvent ev) => UpdateGlows();

    /// <summary>全員の端末で、停電中はマッチを灯された人の周りを明るくする</summary>
    void UpdateGlows()
    {
      bool shouldGlow = !MeetingHud.Instance && !ExileController.Instance && AmongUsUtil.InElecSab;
      foreach (var id in LitPlayers)
      {
        var player = NebulaGameManager.Instance?.GetPlayer(id);
        if (!shouldGlow || player == null || player.IsDead)
        {
          RemoveGlow(id);
          continue;
        }
        if (!Glows.TryGetValue(id, out var glow) || !glow)
        {
          glow = AmongUsUtil.GenerateCustomLight(player.Position.ToUnityVector());
          glow.transform.localScale *= GlowSizeOption.GetValue();
          Glows[id] = glow;
        }
        var pos = player.Position.ToUnityVector();
        glow.transform.position = new UnityEngine.Vector3(pos.x, pos.y, glow.transform.position.z);
      }
    }

    void RemoveGlow(byte id)
    {
      if (Glows.Remove(id, out var glow) && glow)
        UnityEngine.Object.Destroy(glow.gameObject);
    }

    void ClearGlows()
    {
      foreach (var id in Glows.Keys.ToArray())
        RemoveGlow(id);
    }

    [Local]
    void OnMeetingPreSync(MeetingPreSyncEvent ev)
    {
      if (!FreezesOnExile)
        return;
      FreezesOnExile = false;
      var exiled = MeetingHudExtension.ExiledAllModCache ?? [];
      if (!MyPlayer.IsDead && !exiled.Contains(MyPlayer))
        MyPlayer.VanillaPlayer.ModMarkAsExtraVictim(null, FrozenState, FrozenState);
    }

    [OnlyHost]
    void OnCheckKilled(PlayerCheckKilledEvent ev)
    {
      // マッチを灯されたインポスターは、次の会議までキルできない（幻を見せられている）
      if (ev.IsMeetingKill || ev.Killer == ev.Player)
        return;
      if (ev.Killer.IsImpostor && LitPlayers.Contains(ev.Killer.PlayerId))
        ev.Result = KillResult.ObviousGuard;
    }

    void OnGuarded(PlayerGuardEvent ev)
    {
      // キルを封じられたインポスター本人の画面にだけ、幻を見せる
      if (ev.Murderer == null || !LitPlayers.Contains(ev.Murderer.PlayerId))
        return;
      if (ev.Murderer.AmOwner)
        ShowIllusion(ev.Player);

      // 一夜の灯（マッチガール本人の端末で数える）
      if (AmOwner && ev.Murderer.IsImpostor)
      {
        GainTitle("common1");
        if (GuardedKillsToken != null)
          GuardedKillsToken.Value++;
        if (ChallengeToken != null)
          ChallengeToken.Value++;
      }
    }

    /// <summary>
    /// 狙った相手の周りに、生きているクルーの姿を借りた幻を出す（この端末だけ）。
    /// NoS の幽霊役職ハルシネーションと同じく、PlayerDisplay の透明度を上げ下げする
    /// </summary>
    static void ShowIllusion(GamePlayer target)
    {
      AmongUsUtil.PlayCustomFlash(new Virial.Color(1f, 0.6f, 0.2f), 0.1f, 0.5f, 0.35f);

      var center = target.Position.ToUnityVector();
      var models = GamePlayer
        .AllPlayers.Where(p => !p.IsDead && !p.AmOwner)
        .OrderBy(_ => Guid.NewGuid())
        .Take(3)
        .ToArray();
      for (int i = 0; i < models.Length; i++)
      {
        float angle = Mathf.PI * 2f * i / models.Length + Mathf.PI / 2f;
        var pos = center + new UnityEngine.Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 1.1f;
        HudManager.Instance.StartCoroutine(CoIllusion(pos, models[i]).WrapToIl2Cpp());
      }
    }

    static System.Collections.IEnumerator CoIllusion(UnityEngine.Vector2 pos, GamePlayer model)
    {
      var display = VanillaAsset.GetPlayerDisplay();
      UnityHelper.DoForAllChildren(display.gameObject, obj => obj.layer = LayerExpansion.GetPlayersLayer());
      display.transform.position = new UnityEngine.Vector3(pos.x, pos.y, pos.y / 1000f);
      var scale = display.Cosmetics.transform.localScale;
      scale.z = 0.001f;
      display.Cosmetics.transform.localScale = scale;
      var outfit = model.CurrentOutfit.outfit;
      display.UpdateFromPlayerOutfit(outfit, false, true);
      display.Cosmetics.nameText.gameObject.SetActive(true);
      display.Cosmetics.nameText.transform.parent.localPosition = new(0f, 1f, -0.5f);
      display.Cosmetics.nameText.text = outfit.PlayerName;

      var renderers = display.gameObject.GetComponentsInChildren<SpriteRenderer>();
      void SetAlpha(float a)
      {
        foreach (var r in renderers)
          if (r)
            r.color = new UnityEngine.Color(1f, 1f, 1f, a);
      }

      for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f)
      {
        SetAlpha(t);
        yield return null;
      }
      SetAlpha(1f);
      yield return Effects.Wait(2.5f);
      for (float t = 1f; t > 0f; t -= Time.deltaTime / 0.6f)
      {
        SetAlpha(t);
        yield return null;
      }
      if (display)
        UnityEngine.Object.Destroy(display.gameObject);
    }

    [Local]
    void OnTaskComplete(PlayerTaskCompleteLocalEvent ev)
    {
      // タスクを決まった数だけ終えるごとに、マッチを1本補充する（上限あり）
      int need = TasksPerMatchOption.GetValue();
      if (ev.Player != MyPlayer || need <= 0 || MyPlayer.IsDead)
        return;
      if (++TasksSinceRefill < need)
        return;
      TasksSinceRefill = 0;
      if (Matches >= MaxMatchesOption.GetValue())
        return;
      Matches++;
      LightButton?.UpdateUsesIcon(Matches.ToString());
      // 火種の行商
      GainTitle("common2");
    }

    void OnReport(ReportDeadBodyEvent ev)
    {
      // キルされたマッチガールの死体が通報されたときだけ、最後にマッチを灯した相手を全員に知らせる。
      // 追放や凍死では何も知らせない（凍死の死体を通報しても知らせない）
      if (ev.Reported != MyPlayer || LastLitTarget == null || MyPlayer.PlayerState == FrozenState)
        return;
      // 残り火: 最後に灯した相手がインポスターだった
      if (LastLitTarget.IsImpostor)
        GainTitle("another2");
      NebulaAPI
        .CurrentGame?.GetModule<MeetingOverlayHolder>()
        ?.RegisterOverlay(
          NebulaAPI.GUI.VerticalHolder(
            GUIAlignment.Left,
            NebulaAPI.GUI.LocalizedText(GUIAlignment.Left, AttributeAsset.OverlayTitle, "role.matchGirl.ui.reportTitle"),
            NebulaAPI.GUI.RawText(
              GUIAlignment.Left,
              AttributeAsset.OverlayContent,
              Language.Translate("role.matchGirl.ui.reportContent").Replace("%PLAYER%", LastLitTarget.Name)
            )
          ),
          MeetingOverlayHolder.IconsSprite[3],
          ((DefinedAssignable)MyRole).Color
        );
    }

    void OnGameEnd(GameEndEvent ev) => ClearGlows();
  }
}
