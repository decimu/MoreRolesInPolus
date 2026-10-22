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

  // 称号（MyRole を関係する役職に使うので、MyRole より後で登録する）
  private static readonly bool TitlesRegistered = RegisterTitles();

  private static bool RegisterTitles()
  {
    AddonTitles.Register(MyRole, "rebel", "common1", Virial.Achievements.TrophyLevel.Silver);
    AddonTitles.Register(MyRole, "rebel", "common2", Virial.Achievements.TrophyLevel.Gold);
    AddonTitles.Register(MyRole, "rebel", "another1", Virial.Achievements.TrophyLevel.Silver, true);
    AddonTitles.Register(MyRole, "rebel", "challenge", Virial.Achievements.TrophyLevel.Rainbow);
    return true;
  }

  public class Ability : AbstractPlayerUsurpableAbility, IPlayerAbility
  {
    int[] IPlayerAbility.AbilityArguments => [IsUsurped.AsInt()];

    /// <summary>レ・ミゼラブル: 独裁者を処刑させたか（勝敗は試合の終わりに見る）</summary>
    AchievementToken<bool>? ChallengeToken;

    public Ability(GamePlayer player, bool isUsurped)
      : base(player, isUsurped)
    {
      if (!AmOwner || isUsurped)
        return;

      // 反骨の火種: 独裁者が死んだあとも、自分は最後まで生き残る
      new AchievementToken<bool>(
        AddonTitles.Id("rebel", "common1"),
        false,
        (_, _) => !MyPlayer.IsDead && AutocracyPlayers().Any(p => p.Player.IsDead)
      );
      // 不発の革命: 独裁者が一度も独裁会議を開かないまま試合が終わる
      new AchievementToken<bool>(
        AddonTitles.Id("rebel", "another1"),
        false,
        (_, _) => AutocracyPlayers().Any(p => !p.Ability.HasUsed)
      );
      ChallengeToken = new(
        AddonTitles.Id("rebel", "challenge"),
        false,
        (executedAutocracy, _) =>
          executedAutocracy && (NebulaGameManager.Instance?.EndState?.Winners.Test(MyPlayer) ?? false)
      );
    }

    static IEnumerable<(GamePlayer Player, Autocracy.Ability Ability)> AutocracyPlayers() =>
      GamePlayer.AllPlayers
        .Select(p => (Player: p, Ability: p.TryGetAbility<Autocracy.Ability>(out var a) ? a : null))
        .Where(p => p.Ability != null)
        .Select(p => (p.Player, p.Ability!));

    [Local]
    void OnExiled(PlayerExiledEvent ev)
    {
      if (IsUsurped || MyPlayer.IsDead)
        return;

      // 下剋上: 独裁会議で独裁者自身を処刑させた
      if (ev.Player.TryGetAbility<Autocracy.Ability>(out var autocracy) && autocracy.LastMeetingWasAutocracy)
      {
        AddonTitles.Gain("rebel", "common2");
        if (ChallengeToken != null)
          ChallengeToken.Value = true;
      }
    }
  }
}
