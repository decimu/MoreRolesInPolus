using System.Reflection;
using BepInEx;
using UnityEngine;
using Virial.Assignable;
using GamePlayer = Virial.Game.Player;

namespace NoSDevKit;

/// <summary>
/// ゲームフォルダの DevKit.commands に書いた命令を 1 行ずつ実行する（結果は DevKit.log に出る）。
/// 手では作りにくい状況（ダミーに会議を開かせる、役職を変える など）を外から作るためのもの。
/// </summary>
internal static class DevCommands
{
    private static string CommandPath => Path.Combine(Paths.GameRootPath, "DevKit.commands");

    private static long position;
    private static float nextPoll;

    public static void Start()
    {
        // 前回の命令が残っていても実行しない
        File.WriteAllText(CommandPath, "");
        MainThread.OnUpdate += Poll;
        Plugin.Logger.LogInfo($"Command: {CommandPath} に書いた命令を実行します（help で一覧）");
    }

    private static void Poll()
    {
        if (Time.realtimeSinceStartup < nextPoll) return;
        nextPoll = Time.realtimeSinceStartup + 0.2f;

        string text;
        using (var stream = new FileStream(CommandPath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (stream.Length < position) position = 0;
            if (stream.Length == position) return;
            stream.Seek(position, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        var end = text.LastIndexOf('\n');
        if (end < 0) return;
        position += System.Text.Encoding.UTF8.GetByteCount(text.Substring(0, end + 1));

        foreach (var line in text.Substring(0, end).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            try { Execute(line); }
            catch (Exception e) { Plugin.Logger.LogError($"Command: {line} が失敗しました\n{e}"); }
        }
    }

    private static void Execute(string line)
    {
        var args = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (args[0].ToLowerInvariant())
        {
            case "help":
                Plugin.Logger.LogMessage(
                    "Command: 使える命令\n" +
                    "  players                 プレイヤーの一覧（ID・名前・役職・生死）\n" +
                    "  role <player> <役職>    役職を変える（player は ID か me。madmate+rebel のように + で能力を載せられる）\n" +
                    "  meeting <player>        そのプレイヤーに緊急会議を開かせる\n" +
                    "  vote <player> <target>  そのプレイヤーとして投票する（target は ID か skip）\n" +
                    "  discussion <秒>         議論時間を変える（次の会議から）\n" +
                    "  option <設定名> <値>    NoS の設定を変える（例: option role.autocracy.count 1）\n" +
                    "  onlyroles <役職,...>    指定した役職以外を出なくする（変える前の値を記録）\n" +
                    "  restoreroles            onlyroles で変えた出現人数を元に戻す\n" +
                    "  state                   会議の状態（段階・投票ルール）");
                break;

            case "players":
                Plugin.Logger.LogMessage("Command: players\n" + string.Join("\n", GamePlayer.AllPlayers.Select(p =>
                    $"  {p.PlayerId,2} {NameOf(p.PlayerId),-12} {p.Role.Role.InternalName,-16} {p.Role.DisplayName,-12} {(p.IsDead ? "死亡" : "生存")} 位置({PositionOf(p.PlayerId)}){(p.AmOwner ? "（自分）" : "")}")));
                break;

            case "role":
            {
                var player = FindPlayer(args[1]);
                var (role, arguments) = RoleSpec.Parse(args[2]);
                player.SetRole(role, arguments);
                Plugin.Logger.LogMessage($"Command: {NameOf(player.PlayerId)} を {args[2]} にしました");
                break;
            }

            case "meeting":
            {
                var player = FindPlayer(args[1]);
                player.RequestEmergencyMeeting(canInvokeInSabo: true, consumeEmergencyButton: false);
                Plugin.Logger.LogMessage($"Command: {NameOf(player.PlayerId)} に緊急会議を開かせました");
                break;
            }

            case "vote":
            {
                var player = FindPlayer(args[1]);
                byte target = args[2].ToLowerInvariant() == "skip" ? (byte)253 : byte.Parse(args[2]);
                if (!MeetingHud.Instance) throw new InvalidOperationException("会議中ではありません");
                MeetingHud.Instance.CmdCastVote(player.PlayerId, target);
                Plugin.Logger.LogMessage($"Command: {NameOf(player.PlayerId)} として {args[2]} に投票しました");
                break;
            }

            case "option":
            {
                // ホストのみ。ロビーの全員に同期される
                NoSOptions.Set(args[1], args[2]);
                Plugin.Logger.LogMessage($"Command: 設定 {args[1]} を {args[2]} にしました");
                break;
            }

            case "onlyroles":
                NoSOptions.OnlyRoles(args[1], "Command");
                break;

            case "restoreroles":
                NoSOptions.RestoreRoles("Command");
                break;

            case "discussion":
            {
                // 次の会議から有効（ホストのみ）
                var seconds = int.Parse(args[1]);
                GameOptionsManager.Instance.CurrentGameOptions.SetInt(AmongUs.GameOptions.Int32OptionNames.DiscussionTime, seconds);
                Plugin.Logger.LogMessage($"Command: 議論時間を {seconds} 秒にしました（次の会議から）");
                break;
            }

            case "state":
                Plugin.Logger.LogMessage("Command: state\n" + DescribeMeeting());
                break;

            default:
                Plugin.Logger.LogWarning($"Command: 知らない命令です: {line}（help で一覧）");
                break;
        }
    }

    private static GamePlayer FindPlayer(string key)
    {
        if (key.ToLowerInvariant() == "me") return Virial.NebulaAPI.CurrentGame!.LocalPlayer;
        var id = byte.Parse(key);
        return GamePlayer.AllPlayers.FirstOrDefault(p => p.PlayerId == id) ?? throw new ArgumentException($"プレイヤー {key} が見つかりません");
    }

    private static string PositionOf(byte playerId)
    {
        var pc = PlayerControl.AllPlayerControls.ToArray().FirstOrDefault(p => p.PlayerId == playerId);
        if (pc == null) return "-";
        var pos = pc.transform.position;
        return $"{pos.x:0.0}, {pos.y:0.0}";
    }

    private static string NameOf(byte playerId) =>
        PlayerControl.AllPlayerControls.ToArray().FirstOrDefault(p => p.PlayerId == playerId)?.Data?.PlayerName ?? $"#{playerId}";

    private static string DescribeMeeting()
    {
        if (!MeetingHud.Instance) return "  会議中ではありません";

        var ext = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Nebula")?.GetType("Nebula.Extensions.MeetingHudExtension");
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        object? Get(string name) => ext?.GetField(name, flags)?.GetValue(null) ?? ext?.GetProperty(name, flags)?.GetValue(null);

        var canVoteMask = Get("CanVoteMask") is int mask ? mask : -1;
        var voters = GamePlayer.AllPlayers.Where(p => (canVoteMask & (1 << p.PlayerId)) != 0).Select(p => NameOf(p.PlayerId));
        var votes = MeetingHud.Instance.playerStates.ToArray().Where(s => s.DidVote).Select(s => $"{NameOf((byte)s.PlayerId)}→{((byte)s.VotedForId == 253 ? "skip" : NameOf((byte)s.VotedForId))}");

        return $"  段階: {MeetingHud.Instance.CurrentState}\n" +
               $"  スキップ可: {Get("CanSkip")} / 同数でも追放: {Get("ExileEvenIfTie")} / 投票時間: {Get("VotingTimer"):0.0}\n" +
               $"  投票できる人: {string.Join(", ", voters)}\n" +
               $"  投票済み: {string.Join(", ", votes)}";
    }
}
