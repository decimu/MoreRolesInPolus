using System.Collections.Concurrent;
using UnityEngine;

namespace NoSDevKit;

/// <summary>
/// 別スレッドの処理結果をメインスレッドで実行するためのキュー
/// </summary>
public class MainThread : MonoBehaviour
{
    private static readonly ConcurrentQueue<Action> queue = new();

    public MainThread(IntPtr ptr) : base(ptr) { }

    public static void Post(Action action) => queue.Enqueue(action);

    /// <summary>毎フレーム呼ばれる処理</summary>
    public static event Action? OnUpdate;

    public void Update()
    {
        try { OnUpdate?.Invoke(); }
        catch (Exception e) { Plugin.Logger.LogError(e); }

        while (queue.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception e) { Plugin.Logger.LogError(e); }
        }
    }
}
