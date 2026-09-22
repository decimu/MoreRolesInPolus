using System;
using System.Linq;

namespace MoreRolesInPolus.Scripts.Utils
{
    public static class CallMeetingHelper
    {
        private static DateTime lastMeetingCallTime = DateTime.MinValue;

        [NebulaRPC]
        public static void CallMeeting(GamePlayer p)
        {
            if (AmongUsClient.Instance.AmHost && !MeetingHud.Instance)
            {
                if ((DateTime.UtcNow - lastMeetingCallTime).TotalSeconds < 5.0) return;
                lastMeetingCallTime = DateTime.UtcNow;

                var player = PlayerControl.AllPlayerControls.GetFastEnumerator().FirstOrDefault(c => c.PlayerId == p.PlayerId);
                if (player == null) return;
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
