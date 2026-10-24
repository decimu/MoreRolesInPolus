using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using BepInEx;
using Mono.Cecil;
using MonoMod.RuntimeDetour;

namespace NoSDevKit.HotReload;

/// <summary>
/// アドオンのソースを監視し、保存されたらメソッド本体だけをその場で差し替える
/// </summary>
internal static class HotReloader
{
    private static string scriptsDir = "";
    private static string moduleName = "";
    private static bool useHiddenMembers;

    private static FileSystemWatcher? watcher;
    private static Timer? debounce;
    private static int running;
    private static int generation;

    private static ModuleDefinition? original;
    private static HashSet<string>? originalStructure;
    private static Dictionary<string, string> originalBodies = [];

    /// <summary>適用中のフック。キーはメソッドの FullName</summary>
    private static readonly Dictionary<string, (ILHook Hook, string BodySignature)> hooks = [];

    /// <summary>複数起動したゲームが同じ hot_N.dll に書き込まないよう、起動順ごとに分ける</summary>
    private static string WorkDir => Path.Combine(Paths.CachePath, "NoSDevKit", Instance.Index.ToString());

    public static void Start(string addonDir)
    {
        var metaPath = Path.Combine(addonDir, "addon.meta");
        if (!File.Exists(metaPath))
        {
            Plugin.Logger.LogError($"HotReload: {metaPath} が見つかりません");
            return;
        }

        using (var meta = JsonDocument.Parse(File.ReadAllText(metaPath)))
        {
            var id = meta.RootElement.GetProperty("Id").GetString()!;
            // NoS の命名規則（"Script." + Id の先頭を大文字に）
            moduleName = "Script." + char.ToUpper(id[0]) + id.Substring(1);
        }

        scriptsDir = Path.Combine(addonDir, "Scripts");
        var behaviourPath = Path.Combine(scriptsDir, ".behaviour");
        if (File.Exists(behaviourPath))
        {
            using var behaviour = JsonDocument.Parse(File.ReadAllText(behaviourPath));
            useHiddenMembers = behaviour.RootElement.TryGetProperty("UseHiddenMembers", out var v) && v.GetBoolean();
        }

        Directory.CreateDirectory(WorkDir);
        foreach (var old in Directory.GetFiles(WorkDir, "hot_*.dll")) File.Delete(old);

        watcher = new FileSystemWatcher(scriptsDir, "*.cs") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
        watcher.Changed += (_, _) => Schedule();
        watcher.Created += (_, _) => Schedule();
        watcher.Deleted += (_, _) => Schedule();
        watcher.Renamed += (_, _) => Schedule();
        watcher.EnableRaisingEvents = true;

        Plugin.Logger.LogInfo($"HotReload: {scriptsDir} を監視しています（{moduleName}）");
    }

    /// <summary>エディタは 1 回の保存で複数回書き込むことがあるので、まとめて 1 回にする</summary>
    private static void Schedule()
    {
        debounce?.Dispose();
        debounce = new Timer(_ => Task.Run(Reload), null, 300, Timeout.Infinite);
    }

    private static Assembly? FindRunningAssembly() =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
            a.GetName().Name == moduleName && AssemblyLoadContext.GetLoadContext(a)?.Name == "NoSAssemblyContext");

    private static void Reload()
    {
        if (Interlocked.Exchange(ref running, 1) == 1)
        {
            // 実行中に保存されたら、終わってからもう一度
            Schedule();
            return;
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var running = FindRunningAssembly();
            if (running == null)
            {
                Plugin.Logger.LogWarning($"HotReload: {moduleName} はまだ読み込まれていません");
                return;
            }

            if (original == null)
            {
                original = ModuleDefinition.ReadModule(running.Location, new ReaderParameters { InMemory = true });
                originalStructure = IlTransplanter.StructureOf(original);
                originalBodies = original.GetTypes().SelectMany(t => t.Methods).ToDictionary(m => m.FullName, IlTransplanter.BodySignatureOf);
            }

            var output = Path.Combine(WorkDir, $"hot_{++generation}.dll");
            if (AddonCompiler.Compile(moduleName, scriptsDir, useHiddenMembers, output) == null) return;
            var compileTime = sw.ElapsedMilliseconds;

            using var updated = ModuleDefinition.ReadModule(output, new ReaderParameters { InMemory = true });
            File.Delete(output);

            // 構造が変わっていたら、メソッド本体の差し替えでは対応できない
            var structure = IlTransplanter.StructureOf(updated);
            if (!structure.SetEquals(originalStructure!))
            {
                var added = structure.Except(originalStructure!).Take(5).Select(s => "  + " + s);
                var removed = originalStructure!.Except(structure).Take(5).Select(s => "  - " + s);
                Plugin.Logger.LogWarning("HotReload: 型やメンバーの追加・削除・シグネチャ変更があるため、再起動が必要です\n" + string.Join("\n", added.Concat(removed)));
                return;
            }

            List<IlTransplanter.Plan> plans = [];
            List<string> reverts = [];
            List<string> skipped = [];
            foreach (var method in updated.GetTypes().SelectMany(t => t.Methods))
            {
                if (!method.HasBody) continue;
                var key = method.FullName;
                var signature = IlTransplanter.BodySignatureOf(method);
                var isOriginal = originalBodies.TryGetValue(key, out var orig) && orig == signature;
                var hooked = hooks.TryGetValue(key, out var current);

                if (isOriginal)
                {
                    if (hooked) reverts.Add(key);
                    continue;
                }
                if (hooked && current.BodySignature == signature) continue;

                if (method.IsConstructor && method.IsStatic)
                {
                    skipped.Add($"{key}（static 初期化は起動時に実行済みのため反映されません）");
                    continue;
                }

                try
                {
                    plans.Add(IlTransplanter.Prepare(method, signature));
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogWarning($"HotReload: 差し替えられません。再起動が必要です\n  {key}\n  {e.Message}");
                    return;
                }
            }

            foreach (var s in skipped) Plugin.Logger.LogWarning("HotReload: " + s);

            if (plans.Count == 0 && reverts.Count == 0)
            {
                Plugin.Logger.LogInfo($"HotReload: 変更されたメソッドはありません（{sw.ElapsedMilliseconds} ms）");
                return;
            }

            MainThread.Post(() => Apply(plans, reverts, compileTime, sw));
        }
        catch (Exception e)
        {
            Plugin.Logger.LogError("HotReload: 失敗しました\n" + e);
        }
        finally
        {
            Interlocked.Exchange(ref running, 0);
        }
    }

    /// <summary>メインスレッドで実行する。フレームの合間に差し替えることで、実行途中のメソッドと競合しにくくする</summary>
    private static void Apply(List<IlTransplanter.Plan> plans, List<string> reverts, long compileTime, Stopwatch sw)
    {
        foreach (var key in reverts)
        {
            hooks[key].Hook.Dispose();
            hooks.Remove(key);
        }

        List<string> applied = [];
        foreach (var plan in plans)
        {
            var key = plan.Source.FullName;
            try
            {
                var hook = new ILHook(plan.Target, il => IlTransplanter.Transplant(il, plan));
                if (hooks.Remove(key, out var old)) old.Hook.Dispose();
                hooks[key] = (hook, plan.BodySignature);
                applied.Add(plan.Target.DeclaringType?.Name + "." + plan.Target.Name);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"HotReload: {key} の差し替えに失敗しました\n{e}");
            }
        }

        var lines = applied.Select(a => "  ~ " + a).Concat(reverts.Select(r => "  ↩ " + r));
        Plugin.Logger.LogMessage($"HotReload: {applied.Count} 件を差し替え、{reverts.Count} 件を元に戻しました（コンパイル {compileTime} ms / 合計 {sw.ElapsedMilliseconds} ms）\n" + string.Join("\n", lines));
    }
}
