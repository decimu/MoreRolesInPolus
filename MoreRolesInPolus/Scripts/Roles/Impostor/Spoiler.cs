namespace MoreRolesInPolus.Roles.Impostor;

//spoiler キルした相手の役職を確認できるインポスター役職
//
//
public class Spoiler : DefinedSingleAbilityRoleTemplate<Spoiler.Ability>, DefinedRole
{
  private const string overlayKey = "role.spoiler.overlay";

  private Spoiler()
    : base(
      "spoiler",
      NebulaTeams.ImpostorTeam.Color,
      RoleCategory.ImpostorRole,
      NebulaTeams.ImpostorTeam,
      []
    ) { }

  Image? DefinedAssignable.IconImage => iconImage;
  static readonly Image iconImage = NebulaAPI
    .AddonAsset.GetResource(string.Format("Impostor/Spoiler/Spoiler.png"))!
    .AsImage()!;

  public static readonly Spoiler MyRole = new();

  AbilityAssignmentStatus DefinedRole.AssignmentStatus => AbilityAssignmentStatus.KillersSide;
  MultipleAssignmentType DefinedRole.MultipleAssignment => MultipleAssignmentType.Allowed;

  public override Ability CreateAbility(GamePlayer player, int[] arguments) =>
    new Ability(player, arguments.GetAsBool(0));

  //オプション：残りのその役職の人数も表示するか
  private static readonly BoolConfiguration CanSeeRemainingRoles =
    NebulaAPI.Configurations.Configuration("options.role.spoiler.canSeeRemainingRoles", true);

  public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
  {
    public Ability(Virial.Game.Player player, bool isUsurped)
      : base(player, isUsurped) { }

    int[] IPlayerAbility.AbilityArguments => [IsUsurped.AsInt()];

    Dictionary<Player, (DefinedRole role, int roleCount)> roleMap = [];

    //キルク短い役職でキルしたときオーバーレイを上書きさせる
    int killCount = 0;

    [OnlyMyPlayer]
    void OnKillPlayer(PlayerKillPlayerEvent ev)
    {
      if (AmOwner && !IsUsurped && ev.Player != ev.Dead)
      {
        var targetRole = ev.Dead.Role.ExternalRecognitionRole;
        int roleCount = Player.AllPlayers.Count(p =>
          p.IsAlive && p.Role.ExternalRecognitionRole == targetRole
        );
        roleMap[ev.Dead] = (targetRole, roleCount);

        killCount++;

        int nowCount = killCount;

        var lifespan = FunctionalLifespan.GetTimeLifespan(7f);

        bool Isalive()
        {
          return nowCount == killCount && lifespan.IsAliveObject;
        }

        NebulaAPI.GUI.ShowStickerOverlay(
          NebulaAPI.GUI.RawText(
            GUIAlignment.Center,
            AttributeAsset.OverlayContent,
            CanSeeRemainingRoles
              ? targetRole.DisplayColoredName
                + NebulaAPI.Language.Translate(overlayKey).Replace("%COUNT%", roleCount.ToString())
              : targetRole.DisplayColoredName
          ),
          ev.Player.Position,
          () => !Isalive(),
          Isalive
        ); //表示するたびにカウンター増やす　これが何番目かは覚える　
      }
    }

    void ReflectRoleName(PlayerSetFakeRoleNameEvent ev)
    {
      if (!IsUsurped && roleMap.ContainsKey(ev.Player))
      {
        var targetRole = roleMap[ev.Player];
        ev.Alternate(
          CanSeeRemainingRoles
            ? targetRole.role.DisplayColoredName
              + NebulaAPI
                .Language.Translate(overlayKey)
                .Replace("%COUNT%", targetRole.roleCount.ToString())
            : targetRole.role.DisplayColoredName
        );
      }
    }
  }
}
