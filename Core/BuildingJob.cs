using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace UnpleasantMedia.UnpleasantBuildings;

/// <summary>
/// Long work over many items, the editor going on meanwhile: each item read from the scene on the
/// main thread (a little each frame, so the editor keeps up), the heavy part of it done on a thread
/// of its own, and what they made put in afterwards back on the main thread. Its progress is printed
/// in the Output every few seconds.
/// </summary>
public static class BuildingJob
{
    /// <summary>How often its progress is printed, in milliseconds.</summary>
    private const ulong ProgressEvery = 5000;

    /// <summary>The jobs under way, by what they are called: one of each at a time.</summary>
    private static readonly HashSet<string> Running = [];

    /// <summary>How long a frame may spend reading items from the scene, in milliseconds.</summary>
    private const ulong FrameBudget = 25;

    /// <summary>
    /// Starts it: <paramref name="read"/> for each item on the main thread, <paramref name="work"/> on
    /// what that gave on a thread, then <paramref name="apply"/> with all they made, on the main thread
    /// (and <paramref name="after"/> whatever happened). False if one called the same is under way.
    /// </summary>
    public static bool Start<TItem, TRead, TMade>(Node anchor, string name, IReadOnlyList<TItem> items,
        Func<TItem, TRead> read, Func<TRead, TMade> work, Action<List<TMade>> apply, Action? after = null)
    {
        if (!Running.Add(name))
        {
            GD.PushWarning($"{name}: already under way; wait for it to finish.");
            after?.Invoke();
            return false;
        }

        var started = Time.GetTicksMsec();
        var tasks = new List<Task<TMade>>();
        var next = 0;
        var shownAt = Time.GetTicksMsec();
        var tree = anchor.GetTree();
        GD.Print($"{name}...");
        Action? poll = null;
        poll = () =>
        {
            try
            {
                // Read a few more from the scene, as long as the frame allows; each worked on at once.
                var frame = Time.GetTicksMsec();
                while (next < items.Count && Time.GetTicksMsec() - frame < FrameBudget)
                {
                    var made = read(items[next++]);
                    tasks.Add(Task.Run(() => work(made)));
                }
            }
            catch (Exception e)
            {
                GD.PushError($"{name} failed: {e.Message}");
                next = items.Count;
                tasks.Clear();
            }

            var done = 0;
            foreach (var task in tasks)
                if (task.IsCompleted)
                    done++;
            var percent = items.Count == 0 ? 100 : (int)((next + done) * 50L / items.Count);
            if (next < items.Count || done < tasks.Count)
            {
                if (Time.GetTicksMsec() - shownAt >= ProgressEvery)
                {
                    shownAt = Time.GetTicksMsec();
                    GD.Print($"{name}... {percent}%");
                }

                return;
            }

            tree.ProcessFrame -= poll;
            Running.Remove(name);
            try
            {
                var results = new List<TMade>();
                foreach (var task in tasks)
                {
                    if (task.IsFaulted)
                        GD.PushError($"{name}: part of it failed: {task.Exception?.GetBaseException().Message}");
                    else
                        results.Add(task.Result);
                }

                apply(results);
            }
            catch (Exception e)
            {
                GD.PushError($"{name} failed: {e.Message}");
            }
            finally
            {
                after?.Invoke();
                GD.Print($"{name}: done ({(Time.GetTicksMsec() - started) / 1000.0:0.0} s).");
            }
        };
        tree.ProcessFrame += poll;
        return true;
    }
}
