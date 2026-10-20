namespace NoSDevKit;

/// <summary>
/// NoS の設定（共有設定）を ID で書き換える。例: role.autocracy.count=1
/// ホストが書き換えると、ロビーの全員に同期される。
/// </summary>
internal static class NoSOptions
{
    public static void Set(string id, string value)
    {
        var configs = Virial.NebulaAPI.Configurations;

        if (configs.GetSharableVariable<int>(id) is { } intVar)
        {
            intVar.CurrentValue = int.Parse(value);
            return;
        }
        if (configs.GetSharableVariable<bool>(id) is { } boolVar)
        {
            boolVar.CurrentValue = bool.Parse(value);
            return;
        }
        if (configs.GetSharableVariable<float>(id) is { } floatVar)
        {
            floatVar.CurrentValue = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            return;
        }
        throw new ArgumentException($"設定 {id} が見つかりません");
    }

    private static string RoleBackupPath => Path.Combine(BepInEx.Paths.GameRootPath, "DevKit.roles.bak");

    /// <summary>
    /// 指定した役職（カンマ区切りの内部名）以外の出現人数を 0 にし、指定した役職は最低 1 人にする。
    /// 変える前の値は DevKit.roles.bak に残し、RestoreRoles で戻せる（記録が残っていれば上書きしない）。
    /// </summary>
    public static void OnlyRoles(string list, string logPrefix)
    {
        var keep = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (keep.Count == 0) return;

        var configs = Virial.NebulaAPI.Configurations;
        var backup = new List<string>();
        foreach (var role in Virial.NebulaAPI.Assignables.AllRoles)
        {
            var id = "role." + role.InternalName + ".count";
            if (configs.GetSharableVariable<int>(id) is not { } count) continue;
            var target = keep.Contains(role.InternalName) ? Math.Max(1, count.CurrentValue) : 0;
            if (count.CurrentValue == target) continue;
            backup.Add($"{id}={count.CurrentValue}");
            count.CurrentValue = target;
        }

        if (!File.Exists(RoleBackupPath)) File.WriteAllText(RoleBackupPath, string.Join(";", backup));
        Plugin.Logger.LogInfo($"{logPrefix}: {string.Join(", ", keep)} 以外の役職を出なくしました（{backup.Count} 件。元に戻すには restoreroles）");
    }

    /// <summary>OnlyRoles で変える前の出現人数に戻す</summary>
    public static void RestoreRoles(string logPrefix)
    {
        if (!File.Exists(RoleBackupPath))
        {
            Plugin.Logger.LogWarning($"{logPrefix}: 戻す記録（DevKit.roles.bak）がありません");
            return;
        }
        SetAll(File.ReadAllText(RoleBackupPath), logPrefix);
        File.Delete(RoleBackupPath);
        Plugin.Logger.LogMessage($"{logPrefix}: 役職の出現人数を元に戻しました");
    }

    /// <summary>"id=value;id=value" の形をまとめて反映する</summary>
    public static void SetAll(string list, string logPrefix)
    {
        foreach (var entry in list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2)
            {
                Plugin.Logger.LogWarning($"{logPrefix}: 設定の書き方が違います（id=値）: {entry}");
                continue;
            }
            try
            {
                Set(pair[0], pair[1]);
                Plugin.Logger.LogInfo($"{logPrefix}: 設定 {pair[0]} を {pair[1]} にしました");
            }
            catch (Exception e) when (e is ArgumentException or FormatException)
            {
                Plugin.Logger.LogWarning($"{logPrefix}: {e.Message}");
            }
        }
    }
}
