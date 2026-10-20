using System.Diagnostics;
using System.Text.RegularExpressions;
using BepInEx;

namespace NoSDevKit.HotReload;

/// <summary>
/// NoS がアドオン読み込み時に使うのと同じ AddonScriptCompiler.exe でコンパイルする
/// </summary>
internal static class AddonCompiler
{
    private static string? FindCompiler()
    {
        string[] candidates = [
            Path.Combine(Paths.GameRootPath, "BepInEx", "tools", "AddonScriptCompiler.exe"),
            Path.Combine(Paths.GameRootPath, "Tools", "AddonScriptCompiler.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <returns>成功時は出力 DLL のパス。失敗時は null（エラーはログに出す）</returns>
    public static string? Compile(string moduleName, string scriptsDir, bool useHiddenMembers, string outputPath)
    {
        var compiler = FindCompiler();
        if (compiler == null)
        {
            Plugin.Logger.LogError("HotReload: AddonScriptCompiler.exe が見つかりません");
            return null;
        }

        var references = Path.Combine(Paths.GameRootPath, "Cache", "Dll", "references.txt");
        if (!File.Exists(references))
        {
            Plugin.Logger.LogError($"HotReload: {references} がありません（NoS が一度アドオンを読み込むと作られます）");
            return null;
        }

        if (File.Exists(outputPath)) File.Delete(outputPath);

        var args = $"--module-name \"{moduleName}\" --source-dir \"{scriptsDir}\" --output \"{outputPath}\" --config \"{references}\"";
        if (useHiddenMembers) args += " --use-hidden-members";

        var psi = new ProcessStartInfo(compiler, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Paths.GameRootPath,
        };

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        // 診断結果は標準出力ではなく <出力先>.log に書かれる
        var logPath = outputPath + ".log";
        var compileLog = File.Exists(logPath) ? File.ReadAllLines(logPath) : [];
        if (File.Exists(logPath)) File.Delete(logPath);

        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            var errors = compileLog.Select(l => FormatDiagnostic(l, scriptsDir)).OfType<string>().ToArray();
            if (errors.Length == 0) errors = [(stdoutTask.Result + "\n" + stderrTask.Result).Trim()];
            Plugin.Logger.LogError($"HotReload: コンパイルエラー（{errors.Length} 件）\n" + string.Join("\n", errors));
            return null;
        }
        return outputPath;
    }

    // 例: [Error] CS1525: Invalid expression term ';' at Settings\MRIPInfo.cs (Line: 66, Character: 45)
    private static readonly Regex diagnostic = new(@"^\[Error\] (CS\d+): (.*) at (.+) \(Line: (\d+), Character: (\d+)\)\s*$");

    /// <summary>エディタのターミナルでクリックして飛べる「パス:行:列」の形にする</summary>
    private static string? FormatDiagnostic(string line, string scriptsDir)
    {
        var m = diagnostic.Match(line.TrimStart('﻿'));
        if (!m.Success) return null;
        var path = Path.Combine(scriptsDir, m.Groups[3].Value);
        return $"{path}:{m.Groups[4].Value}:{m.Groups[5].Value} {m.Groups[1].Value}: {m.Groups[2].Value}";
    }
}
