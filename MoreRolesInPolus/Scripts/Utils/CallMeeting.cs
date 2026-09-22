using System;
using System.Linq;
using Virial.Runtime;

namespace MoreRolesInPolus.Scripts.Utils
{
  // ゲームごとのクールダウン状態を持つモジュール。
  // static フィールドだと前のゲームの呼び出し時刻が新しいゲームに漏れ、
  // ゲーム開始直後の緊急会議が誤って抑制されうるため IModule 化する。
  [NebulaPreprocess(PreprocessPhase.PostLoadAddons)]
  public static class CallMeetingHelper
  {
    static CallMeetingHelper() => DIManager.Instance.RegisterModule(() => new CooldownModule());

    private class CooldownModule : AbstractModule<Virial.Game.Game>, IModule
    {
      public DateTime LastMeetingCallTime = DateTime.MinValue;
    }

    [NebulaRPC]
    public static void CallMeeting(GamePlayer p)
    {
      if (AmongUsClient.Instance.AmHost && !MeetingHud.Instance)
      {
        var cooldown = NebulaAPI.CurrentGame?.GetModule<CooldownModule>();
        if (cooldown == null)
          return;
        if ((DateTime.UtcNow - cooldown.LastMeetingCallTime).TotalSeconds < 5.0)
          return;
        cooldown.LastMeetingCallTime = DateTime.UtcNow;

        var player = PlayerControl
          .AllPlayerControls.GetFastEnumerator()
          .FirstOrDefault(c => c.PlayerId == p.PlayerId);
        if (player == null)
          return;
        MeetingRoomManager.Instance.AssignSelf(player, null);
        if (GameManager.Instance.CheckTaskCompletion())
        {
          return;
        }
        DestroyableSingleton<HudManager>.Instance.OpenMeetingRoom(player);
        player.RpcStartMeeting(null);
      }
    }
  }
}
