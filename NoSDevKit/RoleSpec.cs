using Virial.Assignable;

namespace NoSDevKit;

/// <summary>
/// 設定や命令で書く役職の指定を解釈する。
/// "autocracy" のような内部名のほか、"madmate+rebel" のように + でつなぐと、前の役職（狂人など）に後ろの役職の能力を載せる。
/// </summary>
internal static class RoleSpec
{
    public static (DefinedRole Role, int[]? Arguments) Parse(string spec)
    {
        var parts = spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new ArgumentException("役職が指定されていません");

        var role = Find(parts[0]);
        if (parts.Length == 1) return (role, null);

        // NoS の狂人などが「載せる役職」を受け取る引数の形（役職の ID ＋ その役職の既定の引数）
        var loaded = Find(parts[1]);
        return (role, [loaded.Id, .. loaded.DefaultAssignableArguments ?? []]);
    }

    private static DefinedRole Find(string name)
    {
        var role = Virial.NebulaAPI.Assignables.GetRole(name);
        if (role != null) return role;
        var names = string.Join(", ", Virial.NebulaAPI.Assignables.AllRoles.Select(r => r.InternalName));
        throw new ArgumentException($"役職「{name}」が見つかりません。指定できる名前: {names}");
    }
}
