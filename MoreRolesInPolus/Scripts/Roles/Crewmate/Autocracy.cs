using MoreRolesInPolus.Scripts.Core;

namespace MoreRolesInPolus.Roles.Crewmate;

public class Autocracy
  : DefinedRoleTemplate,
    DefinedSingleAbilityRole<Autocracy.Ability>,
    DefinedRole,
    HasCitation,
    IAssignableDocument
{
  private Autocracy()
    : base(
      "autocracy",
      new(84, 77, 44),
      RoleCategory.CrewmateRole,
      NebulaTeams.CrewmateTeam,
      [],
      // 原作の「反逆の狂人」にならい、本体の狂人にレブルの能力を載せた状態で必ず 1 人割り当てる
      othersAssignments: () =>
        [
          new(
            (_, _) => (Madmate.MyRole, Madmate.GenerateArgument(Rebel.MyRole)),
            RoleCategory.CrewmateRole
          ),
        ]
    ) { }

  Image? DefinedAssignable.IconImage => iconImage;
  static readonly Image iconImage = NebulaAPI
    .AddonAsset.GetResource(string.Format("Crewmate/Autocracy/Autocracy.png"))!
    .AsImage()!;

  Citation? HasCitation.Citation => AddonCitations.JinroJudgement;

  public Ability CreateAbility(GamePlayer player, int[] arguments) =>
    new Ability(player, arguments.GetAsBool(0), arguments.GetAsBool(1));

  public static Autocracy MyRole = new();
  DefinedRole[] DefinedRole.AdditionalRoles => [Rebel.MyRole];

  bool IAssignableDocument.HasTips => true;
  bool IAssignableDocument.HasAbility => true;

  IEnumerable<AssignableDocumentImage> IAssignableDocument.GetDocumentImages()
  {
    yield return new(autocracySprite, "role.autocracy.ability.autocracy");
  }

  static readonly Image autocracySprite = NebulaAPI
    .AddonAsset.GetResource(string.Format("Crewmate/Autocracy/autocracyButton.png"))!
    .AsImage(115f)!;

  /// <summary>会議中の投票欄に置くボタンの画像。NoS の会議用アイコン（100×110px を 115）と同じ大きさになるよう、2 倍の解像度を 230 で読む</summary>
  static readonly Image meetingSprite = NebulaAPI
    .AddonAsset.GetResource(string.Format("Crewmate/Autocracy/autocracyMeetingButton.png"))!
    .AsImage(230f)!;

  /// <summary>タスク中に発動したことを示す RPC の引数（会議中の発動では処刑先の PlayerId が入る）</summary>
  private const byte NoTarget = byte.MaxValue;

  /// <summary>会議中に発動したときの投票時間。独裁者がすぐに自動で投票するので短くてよい</summary>
  private const float AutocracyVotingTime = 15f;

  /// <summary>タスク中に会議を要求してから、その会議を独裁会議として扱う猶予（「緊急会議！」の演出を含む）</summary>
  private const float TaskMeetingRequestTimeout = 15f;

  [NebulaRPCHolder]
  public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
  {
    int[] IPlayerAbility.AbilityArguments => [IsUsurped.AsInt(), Used.AsInt()];

    /// <summary>能力を使ったか（1 回限り）。役職が入れ替わっても引き継ぐ</summary>
    bool Used;

    /// <summary>今の会議が独裁会議か。会議が実際に始まってから RPC で全員に立つ</summary>
    bool InvokedSpecialMeeting = false;

    /// <summary>タスク中に会議を要求した時刻。要求が通らなかった場合に後の会議を誤判定しないよう、猶予つきで扱う</summary>
    float? TaskMeetingRequestedAt = null;

    ModAbilityButton? AutocracyButton;

    public Ability(GamePlayer player, bool isUsurped, bool used)
      : base(player, isUsurped)
    {
      Used = used;

      if (AmOwner && !Used)
      {
        AutocracyButton = NebulaAPI.Modules.AbilityButton(
          this,
          MyPlayer,
          VirtualKeyInput.Ability,
          1f,
          "autocracy",
          autocracySprite
        );

        AutocracyButton.OnClick = (button) =>
        {
          if (Used || MeetingHud.Instance)
            return;
          TaskMeetingRequestedAt = Time.realtimeSinceStartup;
          MyPlayer.RequestEmergencyMeeting(canInvokeInSabo: true, consumeEmergencyButton: false);
        };
      }
    }

    [NebulaRPC]
    public static void RpcAutocracyMeeting(GamePlayer caller, byte targetId)
    {
      if (caller.TryGetAbility<Ability>(out var ability))
      {
        ability.BeginAutocracy(targetId);
      }
    }

    /// <summary>全員の端末で呼ばれ、この会議を独裁会議にする</summary>
    void BeginAutocracy(byte targetId)
    {
      Used = true;
      InvokedSpecialMeeting = true;

      if (targetId != NoTarget)
      {
        // 会議中の発動：議論を打ち切って投票時間に移る（スキップ不可・同数でも追放）
        Nebula.Patches.MeetingModRpc.RpcChangeVotingStyle.LocalInvoke(
          (0xFFFFFFF, false, AutocracyVotingTime, true, false)
        );
      }
      else
      {
        // タスク中の発動：議論はそのまま残し、投票のルールだけ変える
        MeetingHudExtension.CanSkip = false;
        MeetingHudExtension.ExileEvenIfTie = true;
      }

      // 独裁者だけが投票できる
      MeetingHudExtension.UpdateCanVoteMask(1 << MyPlayer.PlayerId);

      if (!AmOwner)
        return;

      AutocracyButton?.Break();

      // 反逆者が生存していると、独裁者は指定先ではなく自分自身に投票する（＝自分が処刑される）
      // レブルは狂人に載った能力なので、役職ではなく能力の有無で判定する
      bool hasAliveRebel = NebulaGameManager.Instance!.AllPlayerInfo.Any(player =>
        !player.IsDead && player.TryGetAbility<Rebel.Ability>(out _)
      );
      if (hasAliveRebel)
        VoteWhenVotingStarts(MyPlayer.PlayerId);
      else if (targetId != NoTarget)
        VoteWhenVotingStarts(targetId);
    }

    void VoteWhenVotingStarts(byte targetId)
    {
      System.Collections.IEnumerator CoVote()
      {
        while (
          MeetingHud.Instance
          && MeetingHud.Instance.CurrentState != MeetingHud.MeetingStates.NotVoted
        )
          yield return null;
        if (MeetingHud.Instance)
          MeetingHud.Instance.Confirm(targetId);
      }
      MeetingHud.Instance.StartCoroutine(CoVote().WrapToIl2Cpp());
    }

    [Local]
    void OnMeetingStart(MeetingStartEvent ev)
    {
      // タスク中に要求した会議が、実際に自分の緊急会議として始まったときだけ独裁会議にする
      if (TaskMeetingRequestedAt is float requestedAt)
      {
        TaskMeetingRequestedAt = null;
        var meeting = NebulaAPI.CurrentGame?.CurrentMeeting;
        if (
          !Used
          && Time.realtimeSinceStartup - requestedAt < TaskMeetingRequestTimeout
          && meeting != null
          && meeting.InvokedBy == MyPlayer
          && meeting.ReportedDeadBody == null
        )
        {
          RpcAutocracyMeeting(MyPlayer, NoTarget);
          return;
        }
      }

      if (!Used)
        CreateDiscussionButtons();
    }

    /// <summary>会議中の発動に使う、各プレイヤーの投票欄のボタン</summary>
    readonly List<(GamePlayer Target, GameObject Button)> DiscussionButtons = [];

    /// <summary>
    /// 会議中の発動：処刑先のボタンを押す。議論中か、投票時間中で自分がまだ投票していない間だけ使える。
    /// （多くの村は議論時間が 0 秒で、投票時間が実質の議論時間になっているため）
    /// NoS の MeetingPlayerButtonManager は議論中に押せないため、同じ作り方で独自に置く。
    /// </summary>
    void CreateDiscussionButtons()
    {
      DiscussionButtons.Clear();
      foreach (var area in MeetingHud.Instance.playerStates)
      {
        var target = NebulaGameManager.Instance?.GetPlayer(area.PlayerId.Value);
        if (target == null || target.AmOwner)
          continue;

        var template = area.Buttons.transform.Find("CancelButton").gameObject;
        var obj = UnityEngine.Object.Instantiate(template, area.transform);
        obj.name = "AutocracyButton";
        obj.transform.localPosition = MeetingHudExtension.VoteAreaPlayerIconPos;
        obj.GetComponent<SpriteRenderer>().sprite = meetingSprite.GetSprite();

        var button = obj.GetComponent<PassiveButton>();
        button.OnClick.RemoveAllListeners();
        button.OnClick.AddListener(() => OnClickDiscussionButton(target));

        obj.SetActive(false);
        DiscussionButtons.Add((target, obj));
      }
    }

    void OnClickDiscussionButton(GamePlayer target)
    {
      if (!CanUseDiscussionButtons() || target.IsDead || !MeetingHudExtension.CanInvokeSomeAction)
        return;
      if (IsUsurped)
      {
        Used = true;
        return;
      }
      RpcAutocracyMeeting(MyPlayer, target.PlayerId);
    }

    bool CanUseDiscussionButtons() =>
      !Used
      && !MyPlayer.IsDead
      && MeetingHud.Instance
      && MeetingHud.Instance.CurrentState
        is MeetingHud.MeetingStates.Discussion
          or MeetingHud.MeetingStates.NotVoted
      && !MeetingHudExtension.ExileEvenIfTie;

    [Local]
    void OnGameUpdate(GameUpdateEvent ev)
    {
      if (DiscussionButtons.Count == 0)
        return;
      if (!MeetingHud.Instance)
      {
        DiscussionButtons.Clear();
        return;
      }

      bool canUse = CanUseDiscussionButtons();
      foreach (var (target, button) in DiscussionButtons)
      {
        if (button)
          button.SetActive(canUse && !target.IsDead);
      }
    }

    [OnlyHost]
    void OnVote(PlayerVoteCastEvent ev)
    {
      // 独裁者本人が投票した時点で投票を締め切る
      if (!InvokedSpecialMeeting || ev.Voter != MyPlayer)
        return;

      System.Collections.IEnumerator CoWaitAndEnd()
      {
        yield return null;
        NebulaAPI.CurrentGame?.CurrentMeeting?.EndVotingForcibly(true);
      }
      MeetingHud.Instance.StartCoroutine(CoWaitAndEnd().WrapToIl2Cpp());
    }

    [OnlyHost]
    void OnFixVote(PlayerFixVoteHostEvent ev)
    {
      // 独裁会議では独裁者の 1 票だけで決める。投票操作は画面側でも止めているが、
      // それをすり抜けた票（フリープレイのダミーなど）も集計で数えない
      if (!InvokedSpecialMeeting || ev.Player == MyPlayer)
        return;
      ev.DidVote = false;
    }

    void OnCanGuess(PlayerCanGuessPlayerLocalEvent Ev)
    {
      if (!InvokedSpecialMeeting)
        return;
      Ev.CanGuess = false;
    }

    void OnMeetingEnd(MeetingEndEvent ev) => InvokedSpecialMeeting = false;
  }
}
