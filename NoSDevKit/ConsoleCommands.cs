using System.Reflection;
using Virial.Command;
using Virial.Compat;

namespace NoSDevKit;

/// <summary>
/// DevKit の命令（DevKit.commands と同じもの）を、NoS のゲーム内コンソール（左 Ctrl + Enter）から打てるようにする。
/// NoS は名前空間を省いたコマンドを nebula:: から探すので、そこに登録する（devkit:: でも呼べる）。
/// NoS 本体にある role は登録しない。
/// </summary>
internal static class ConsoleCommands
{
    private static readonly string[] Names = ["devkit", "players", "meeting", "vote", "discussion", "option", "onlyroles", "restoreroles", "state"];

    public static void Start() => MainThread.OnUpdate += TryRegister;

    private static void TryRegister()
    {
        var nebula = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Nebula");
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        // NoS 本体のコマンドが登録し終わってから足す
        if (nebula?.GetType("Nebula.Modules.NebulaPreprocessorImpl")?.GetProperty("Finished", flags)?.GetValue(null) is not true)
            return;
        MainThread.OnUpdate -= TryRegister;

        var register = nebula.GetType("Nebula.Commands.CommandManager")?.GetMethod("RegisterCommand", flags, [typeof(ICommand), typeof(string[])]);
        if (register == null)
        {
            Plugin.Logger.LogWarning("Console: NoS のコマンドの登録先が見つからないため、コンソールの命令は使えません");
            return;
        }

        foreach (var name in Names)
            register.Invoke(null, [new DevKitCommand(), new[] { "nebula::" + name, "devkit::" + name }]);
        Plugin.Logger.LogInfo("Console: 左 Ctrl + Enter のコンソールから DevKit の命令を打てます（devkit で一覧）");
    }

    private class DevKitCommand : ICommand
    {
        IEnumerable<CommandComplement> ICommand.Complement(string label, IReadOnlyArray<ICommandToken> arguments, string? last, ICommandExecutor executor) => [];

        CoTask<ICommandToken> ICommand.Evaluate(string label, IReadOnlyArray<ICommandToken> arguments, CommandEnvironment env)
        {
            // 引数はふつう文字列のトークンなので、すぐに値が取れる
            var words = arguments.Select(token => token.AsValue<string>(env) is CoImmediateTask<string> s ? s.Result : token.ToString());
            var name = label.Contains("::") ? label[(label.LastIndexOf("::") + 2)..] : label;
            var line = string.Join(' ', words.Prepend(name == "devkit" ? "help" : name));

            try
            {
                DevCommands.Execute(line);
                env.Logger.Push(CommandLogLevel.Info, $"DevKit: {line}（結果は画面左上と DevKit.log）");
            }
            catch (Exception e)
            {
                env.Logger.Push(CommandLogLevel.Error, $"DevKit: {line} が失敗しました: {e.Message}");
            }
            return new CoImmediateTask<ICommandToken>(EmptyCommandToken.Token);
        }
    }
}
