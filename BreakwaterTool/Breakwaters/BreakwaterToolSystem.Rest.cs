using System.Collections.Generic;
using Unity.Entities;
using UnityEngine.InputSystem;

namespace BreakwaterTool.Breakwaters {
  /// <summary>
  /// A slider's values on a breakwater too slow to lay as the slider moves: the panel shows each
  /// at once, and the breakwater is laid once the slider rests, let go.
  /// Only the shown look runs ahead: the record and its stones change together, at the lay, so a
  /// save in between keeps the breakwater as it stands. No other edit drops a waiting value.
  /// </summary>
  internal sealed partial class BreakwaterToolSystem {
    /// <summary>
    /// The layout's own time, in milliseconds, above which a slider lays the picked breakwater only
    /// once it rests: one frame at 30 fps.
    /// </summary>
    // [PERF] The frames after a lay cost about twice its layout's time again, so each value of a
    // drag over a slower breakwater would hold the game for several frames.
    private const double MostLiveLayoutCost = 33.0;

    /// <summary>
    /// How long a slider rests before its value is laid, in seconds and in frames: the panel sends
    /// a drag's value at most every 100 ms and only on a frame, so a gap past both, the mouse's
    /// button up, is a release.
    /// </summary>
    private const float RestSeconds = 0.25f;

    private const int RestFrames = 3;

    // The layout's own time at each breakwater's last lay by the tool, in milliseconds.
    private readonly Dictionary<Entity, double> layoutCosts = [];

    // The parameters a slider set ahead of the lay and their breakwater, null when none wait; when
    // they were set, in real seconds, and the frames since.
    private (Entity Record, BreakwaterParameters Parameters)? waiting;

    private float waitingSince;

    private int waitingFrames;

    /// <summary>
    /// Whether a slider lays a breakwater as it moves: when the layout of its last lay by the tool
    /// was quick enough. One the tool has not laid yet, as one loaded with the city, waits.
    /// </summary>
    private bool IsLaidLive(Entity record) =>
      this.layoutCosts.TryGetValue(record, out var cost) &&
      cost <= BreakwaterToolSystem.MostLiveLayoutCost;

    /// <summary>
    /// Shows parameters a slider set on the picked breakwater, laid once the slider rests.
    /// </summary>
    private void Wait(Entity record, BreakwaterParameters parameters) {
      this.waiting = (record, parameters);
      this.waitingSince = UnityEngine.Time.realtimeSinceStartup;
      this.waitingFrames = 0;
      this.Parameters = parameters;
      this.LookRevision++;
    }

    /// <summary>
    /// Lays what a slider left waiting once it rested and its drag is over; the tool calls it each
    /// frame.
    /// </summary>
    private void LayRested() {
      if (this.waiting is null) {
        return;
      }

      this.waitingFrames++;

      if (this.waitingFrames >= BreakwaterToolSystem.RestFrames &&
        UnityEngine.Time.realtimeSinceStartup - this.waitingSince >=
        BreakwaterToolSystem.RestSeconds &&
        !this.IsPointerHeld()) {
        this.LayWaiting();
      }
    }

    /// <summary>
    /// Whether the mouse's primary button is down, as it is through a drag held still.
    /// The input system keeps reading the mouse with the window out of focus, never letting the
    /// button go there; a release the game never sees leaves the value waiting until the next click
    /// or until the breakwater is left.
    /// </summary>
    private bool IsPointerHeld() {
      var isHeld = Mouse.current?.leftButton.isPressed ?? false;

      this.OnPointerRead(ref isHeld);

      return isHeld;
    }

    partial void OnPointerRead(ref bool isHeld);

    /// <summary>
    /// Lays at once what a slider left waiting, as its breakwater is left.
    /// </summary>
    private void LayWaiting() {
      if (this.waiting is not {} waiting) {
        return;
      }

      if (waiting.Record == this.gestures.Picked) {
        this.EditPicked(waiting.Record, new BreakwaterChange());

        return;
      }

      this.waiting = null;
      this.breakwaters.Edit(waiting.Record, new BreakwaterChange(Parameters: waiting.Parameters));
    }

    /// <summary>
    /// An edit of a breakwater, with the parameters a slider left waiting on it where the edit sets
    /// none.
    /// </summary>
    private BreakwaterChange TakeWaiting(Entity record, BreakwaterChange change) {
      if (this.waiting is not {} waiting || waiting.Record != record) {
        return change;
      }

      this.waiting = null;

      return change with {
        Parameters = change.Parameters ?? waiting.Parameters
      };
    }
  }
}
