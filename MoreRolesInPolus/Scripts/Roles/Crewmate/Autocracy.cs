using MoreRolesInPolus.Scripts.Core;

namespace MoreRolesInPolus.Roles.Crewmate;

public class Autocracy : DefinedRoleTemplate, DefinedSingleAbilityRole<Autocracy.Ability>, DefinedRole, HasCitation, IAssignableDocument
{
  private Autocracy() : base(
    "autocracy",
    new(84, 77, 44),
    RoleCategory.CrewmateRole,
    NebulaTeams.CrewmateTeam,
    [],
    othersAssignments: () => [
      new((_, PlayerId) => (Rebel.MyRole, [PlayerId]), RoleCategory.CrewmateRole)
    ]
  ) { }

  Image? DefinedAssignable.IconImage => iconImage;
  static readonly Image iconImage = NebulaAPI.AddonAsset.GetResource(string.Format("Crewmate/Autocracy/Autocracy.png"))!.AsImage()!;

  Citation? HasCitation.Citation => AddonCitations.JinroJudgement;

  public Ability CreateAbility(GamePlayer player, int[] arguments) => new Ability(player, arguments.GetAsBool(0));

  AbilityAssignmentStatus DefinedRole.AssignmentStatus => AbilityAssignmentStatus.CanLoadToMadmate;

  static public Autocracy MyRole = new();
  DefinedRole[] DefinedRole.AdditionalRoles => [Rebel.MyRole];

  bool IAssignableDocument.HasTips => true;
  bool IAssignableDocument.HasAbility => true;
  IEnumerable<AssignableDocumentImage> IAssignableDocument.GetDocumentImages()
  {
    yield return new(autocracySprite, "role.autocracy.ability.autocracy");
  }

  static readonly Image autocracySprite = NebulaAPI.AddonAsset.GetResource(string.Format("Crewmate/Autocracy/autocracyButton.png"))!.AsImage(115f)!;

  [NebulaRPCHolder]
  public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
  {
    int[] IPlayerAbility.AbilityArguments => [IsUsurped.AsInt()];

    bool InvokedSpecialMeeting = false;

    public Ability(GamePlayer player, bool isUsurped) : base(player, isUsurped)
    {

      if (AmOwner)
      {

        var autocracyButton = NebulaAPI.Modules.AbilityButton(
          this,
          MyPlayer,
          VirtualKeyInput.Ability,
          1f,
          "autocracy",
          autocracySprite
        );

        autocracyButton.OnClick = (button) =>
        {
          InvokeSpecialMeeting();
          button.Break();
        };

      }
    }

    [NebulaRPC]
    public static void RpcAutocracyMeeting(GamePlayer caller)
    {
      if (caller.TryGetAbility<Ability>(out var ability))
      {
        ability.InvokedSpecialMeeting = true;
      }
    }

    void InvokeSpecialMeeting()
    {
      RpcAutocracyMeeting(MyPlayer);
      MyPlayer.RequestEmergencyMeeting(canInvokeInSabo: true, consumeEmergencyButton: false);
    }

    void OnMeeting(MeetingStartEvent ev)
    {
      var meeting = NebulaAPI.CurrentGame?.CurrentMeeting;
      if (meeting == null) return;
      if (meeting.InvokedBy == MyPlayer && meeting.ReportedDeadBody == null && InvokedSpecialMeeting)
      {
        ev.CanVote = GamePlayer.LocalPlayer!.Role.Role == Autocracy.MyRole;
        MeetingHudExtension.ExileEvenIfTie = true;

        bool HasAliveRebel = NebulaGameManager.Instance!.AllPlayerInfo.Any(Player =>
          !Player.IsDead &&
          Player.Role.Role == Rebel.MyRole
        );

        if (AmOwner && HasAliveRebel)
        {
          System.Collections.IEnumerator CoVote()
          {
            while (MeetingHud.Instance.CurrentState != MeetingHud.MeetingStates.NotVoted) yield return null;
            MeetingHud.Instance.Confirm(MyPlayer.PlayerId);
          }
          MeetingHud.Instance.StartCoroutine(CoVote().WrapToIl2Cpp());
        }
      }
    }

    [OnlyHost]
    void OnVote(PlayerVoteCastEvent ev)
    {
      if (InvokedSpecialMeeting)
      {
        System.Collections.IEnumerator CoWaitAndEnd()
        {
          yield return null;
          var meeting = NebulaAPI.CurrentGame?.CurrentMeeting;
          meeting?.EndVotingForcibly(true);
        }
        MeetingHud.Instance.StartCoroutine(CoWaitAndEnd().WrapToIl2Cpp());
      }
    }

    void OnCanGuess(PlayerCanGuessPlayerLocalEvent Ev)
    {
      if (!InvokedSpecialMeeting) return;
      Ev.CanGuess = false;
    }

    void OnMeetingEnd(MeetingEndEvent ev) => InvokedSpecialMeeting = false;
  }
}