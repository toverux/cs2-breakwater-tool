using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game;

namespace BreakwaterTool.Scenarios {
  /// <summary>
  /// Runs a scenario's calls at <see cref="SystemUpdatePhase.ModificationEnd"/>, after the search
  /// tree's update and before the frame's cleanup: where an object created at once would lose its
  /// <c>Created</c> unseen, and crash the game at its next update.
  /// </summary>
  internal sealed partial class LateCallSystem : GameSystemBase {
    private static readonly Queue<(Action Call, TaskCompletionSource<bool> Done)> Calls = new();

    /// <summary>
    /// Runs a call at the system's next update; the task ends with it.
    /// </summary>
    internal static Task Call(Action call) {
      var done = new TaskCompletionSource<bool>();

      LateCallSystem.Calls.Enqueue((call, done));

      return done.Task;
    }

    protected override void OnUpdate() {
      while (LateCallSystem.Calls.Count > 0) {
        var (call, done) = LateCallSystem.Calls.Dequeue();

        // Any failure belongs to the scenario awaiting the call, which reports it.
        try {
          call();
          done.SetResult(true);
        }
        catch (Exception ex) {
          done.SetException(ex);
        }
      }
    }
  }
}
