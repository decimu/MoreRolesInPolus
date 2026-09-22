namespace MoreRolesInPolus.Scripts.Core
{
  public static class AddonCitations
  {
    public static Citation JinroJudgement { get; private set; } = new(
      "jinroJudgement",
      NebulaAPI.AddonAsset.GetResource("Citations/logo_jj.png")?.AsImage(),
      new RawTextComponent("人狼ジャッジメント"),
      "https://www.sorairo.jp/jrvs.html"
    );
  }
}