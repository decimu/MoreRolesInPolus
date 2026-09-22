using Virial;
using Virial.Assignable;
using Virial.Configuration;
using Virial.Events.Game.Meeting;
using Virial.Events.Player;
using Virial.Game;

namespace MoreRolesInPolus.Roles.Modifier;

public class Spoiler : DefinedAllocatableModifierTemplate, DefinedAllocatableModifier
{

  private const string overlayKey = "role.spoiler.overlay";
  private Spoiler() : base("spoiler", "spoiler", new(180, 0, 0), [])
  {

  }

  Image? DefinedAssignable.IconImage => iconImage;
  static readonly Image iconImage = NebulaAPI.AddonAsset.GetResource(string.Format("Impostor/Spoiler/Spoiler.png"))!.AsImage()!;

  static public Spoiler MyRole = new Spoiler();
  RuntimeModifier RuntimeAssignableGenerator<RuntimeModifier>.CreateInstance(GamePlayer player, int[] arguments) => new Instance(player);
  public class Instance : RuntimeAssignableTemplate, RuntimeModifier
  {
    DefinedModifier RuntimeModifier.Modifier => MyRole;
    public Instance(GamePlayer player) : base(player)
    {
    }

    void RuntimeAssignable.OnActivated() { }

    Dictionary<Player, (DefinedRole role, int roleCount)> roleMap = [];



    int killCount = 0;

    [OnlyMyPlayer]
    void OnKillPlayer(PlayerKillPlayerEvent ev)
    {
      if (AmOwner && ev.Player != ev.Dead)
      {
        var targetRole = ev.Dead.Role.ExternalRecognitionRole;
        int roleCount = Player.AllPlayers.Count(p => p.IsAlive && p.Role.ExternalRecognitionRole == targetRole);
        roleMap[ev.Dead] = (targetRole, roleCount);

        killCount++;

        int nowCount = killCount;

        var lifespan = FunctionalLifespan.GetTimeLifespan(7f);

        bool Isalive()
        {
          return nowCount == killCount && lifespan.IsAliveObject;
        }

        NebulaAPI.GUI.ShowStickerOverlay(NebulaAPI.GUI.RawText(GUIAlignment.Center, AttributeAsset.OverlayContent, targetRole.DisplayColoredName + NebulaAPI.Language.Translate(overlayKey).Replace("%COUNT%", roleCount.ToString())), ev.Player.Position, () => !Isalive(), Isalive);//�\�����邽�тɃJ�E���^�[���₷�@���ꂪ���Ԗڂ��͊o����@


      }
    }

    void ReflectRoleName(PlayerSetFakeRoleNameEvent ev)
    {

      if (roleMap.ContainsKey(ev.Player))
      {
        var targetRole = roleMap[ev.Player];
        ev.Alternate(targetRole.role.DisplayColoredName + NebulaAPI.Language.Translate(overlayKey).Replace("%COUNT%", targetRole.roleCount.ToString()));
      }
    }



  }
}