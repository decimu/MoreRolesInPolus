using MoreRolesInPolus.Scripts.Core;

namespace MoreRolesInPolus.Roles.Crewmate;

/// <summary>
/// 人狼ジャッジメントの「反逆の狂人」。独裁者がいるとき、本体の狂人にこの能力を載せて割り当てる。
/// 勝利条件・インポスターの判明・ベントなどは本体の狂人の設定に従い、
/// この能力は「生存していると、独裁会議で独裁者自身が処刑される」ことの目印だけを担う（処理は独裁者側）。
/// </summary>
public class Rebel
  : DefinedRoleTemplate,
    DefinedSingleAbilityRole<Rebel.Ability>,
    DefinedRole,
    HasCitation
{
  private Rebel()
    : base(
      "rebel",
      new(255, 105, 65),
      RoleCategory.CrewmateRole,
      NebulaTeams.CrewmateTeam,
      [],
      withAssignmentOption: false,
      withOptionHolder: false
    ) { }

  Citation? HasCitation.Citation => AddonCitations.JinroJudgement;

  public Ability CreateAbility(GamePlayer player, int[] arguments) =>
    new Ability(player, arguments.GetAsBool(0));

  AbilityAssignmentStatus DefinedRole.AssignmentStatus => AbilityAssignmentStatus.CanLoadToMadmate;

  public static Rebel MyRole = new();

  public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
  {
    int[] IPlayerAbility.AbilityArguments => [IsUsurped.AsInt()];

    public Ability(GamePlayer player, bool isUsurped)
      : base(player, isUsurped) { }
  }
}
