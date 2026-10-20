using System.Diagnostics;

namespace NoSDevKit;

/// <summary>
/// 同時に起動した Among Us の中で、このプロセスが何番目か（1 始まり）。
/// Debug ビルドのタスクは複数のゲームを数秒おきに起動するため、起動時刻の順で決める。
/// </summary>
internal static class Instance
{
    public static readonly int Index = Compute();

    public static bool IsFirst => Index == 1;

    private static int Compute()
    {
        try
        {
            using var me = Process.GetCurrentProcess();
            var started = me.StartTime;
            int earlier = 0;
            foreach (var p in Process.GetProcessesByName(me.ProcessName))
            {
                using (p)
                {
                    try
                    {
                        if (p.Id != me.Id && p.StartTime < started) earlier++;
                    }
                    catch { }
                }
            }
            return earlier + 1;
        }
        catch
        {
            return 1;
        }
    }
}
