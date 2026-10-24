using Virial.Achievements;

namespace MoreRolesInPolus.Scripts.Core
{
  /// <summary>
  /// アドオンの称号。NoS 本体の称号と区別するためにグループを付けて登録する。
  /// 称号の ID は「グループ.役職.キー」、翻訳キーは「achievement.グループ.役職.キー.title」などになる。
  /// </summary>
  public static class AddonTitles
  {
    public const string Group = "moreRolesInPolus";

    public static string Id(string role, string key) => Group + "." + role + "." + key;

    /// <summary>プリプロセス中（役職を作る Roles フェイズ）に呼ぶ</summary>
    public static void Register(
      DefinedAssignable relatedRole,
      string role,
      string key,
      TrophyLevel rank,
      bool isSecret = false
    ) =>
      NebulaAPI.Preprocessor!.Titles.Register(
        new TitleBuilder(Group, role + "." + key)
        {
          Rank = rank,
          IsSecret = isSecret,
          RelatedRole = relatedRole,
        }
      );

    /// <summary>本人の端末で、条件を満たした称号を記録する</summary>
    public static void Gain(string role, string key) => new StaticAchievementToken(Id(role, key));
  }
}
