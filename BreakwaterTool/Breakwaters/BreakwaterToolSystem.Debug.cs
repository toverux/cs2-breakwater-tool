#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BreakwaterTool.Gestures;
using Game.Input;
using Game.Prefabs;
using Game.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace BreakwaterTool.Breakwaters {
  /// <summary>
  /// Debug entry points for the unity eval: the tool's state, its panel's actions, and where a
  /// point of the terrain shows on screen, for a pointer sent there.
  /// </summary>
  internal sealed partial class BreakwaterToolSystem {
    // The game's labels the hints read, by name, null where the game has none.
    private readonly Dictionary<string, IProxyAction?> labelsRead = [];

    // The scripted drag DebugBench started, run from the tool's update.
    private DragBench? bench;

    // The mouse's primary button as DebugHoldPointer fakes it, null to read the mouse.
    private bool? pointerHeld;

    internal static BreakwaterToolSystem Instance =>
      World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BreakwaterToolSystem>();

    /// <summary>
    /// The tool's state on one line.
    /// </summary>
    internal static string DebugState() {
      var tool = BreakwaterToolSystem.Instance;
      var gestures = tool.gestures;

      var points = string.Join(
        " ",
        gestures.Points.Select(static point =>
          FormattableString.Invariant($"{point.x:F1},{point.y:F1}")
        )
      );

      return $"active {tool.IsActive}, {gestures.Mode}, points [{points}], held {gestures.Held}, " +
        $"picked {tool.Picked}, can revert {tool.CanRevert}, hint {tool.Hint}, " +
        $"refusal {(tool.Refusal is {} refusal ? refusal.id ?? "(no id)" : "none")}, " +
        $"ground {tool.Ground}, offer expected {tool.isOfferExpected}, " +
        $"pointer {tool.pointer.At?.ToString() ?? "none"} under {tool.pointer.Under}";
    }

    /// <summary>
    /// What the gesture interpreter holds: its mode, and the outline drawn or picked.
    /// </summary>
    internal static (GestureMode Mode, float2[] Points) DebugGestures() {
      var gestures = BreakwaterToolSystem.Instance.gestures;

      return (gestures.Mode, [.. gestures.Points]);
    }

    /// <summary>
    /// The game's labels the hints bar reads, by name, null where the game has none.
    /// </summary>
    internal static IReadOnlyDictionary<string, IProxyAction?> DebugLabels() =>
      BreakwaterToolSystem.Instance.labelsRead;

    partial void OnLabelRead(string name, IProxyAction? state) => this.labelsRead[name] = state;

    /// <summary>
    /// The chosen stones, by prefab name.
    /// </summary>
    internal static string DebugChoice() =>
      string.Join(
        ", ",
        BreakwaterToolSystem.Instance.GetSelectedPrefabs().Select(static prefab => prefab.name)
      );

    /// <summary>
    /// The look on one line: the style it is and the stones a style set last, the parameters,
    /// whether the picked breakwater's toe was picked by hand, and whether its ground changed.
    /// </summary>
    internal static string DebugLook() {
      var tool = BreakwaterToolSystem.Instance;

      var names = tool.styleStones?.Select(stone =>
        tool.prefabSystem.GetPrefab<PrefabBase>(stone).name
      );

      var remembered = names is null ? "none" : $"[{string.Join(", ", names)}]";

      return $"style {tool.Style ?? "none"}, style stones {remembered}, " +
        $"{ParameterTable.Write(tool.Parameters)}, " +
        $"toe by hand {tool.gestures.Toe is not null}, outdated {tool.IsOutdated}, " +
        $"revision {tool.LookRevision}";
    }

    /// <summary>
    /// Forgets the look read from the settings, as a new session starts without it: the tool's next
    /// activation reads it again.
    /// </summary>
    internal static void DebugForgetLook() {
      var tool = BreakwaterToolSystem.Instance;

      tool.isLookLoaded = false;
      tool.Parameters = BreakwaterParameters.Default;
      tool.choice.Clear();
    }

    /// <summary>
    /// Sets the layout time the tool measured at a breakwater's last lay, which decides whether a
    /// slider lays it as it moves.
    /// </summary>
    internal static void DebugLayoutCost(Entity record, double milliseconds) =>
      BreakwaterToolSystem.Instance.layoutCosts[record] = milliseconds;

    /// <summary>
    /// Fakes the mouse's primary button, held or not, where a slider's wait reads it; null reads
    /// the mouse again.
    /// </summary>
    internal static void DebugHoldPointer(bool? isHeld) =>
      BreakwaterToolSystem.Instance.pointerHeld = isHeld;

    /// <summary>
    /// Whether a slider's wait reads the mouse's primary button as held.
    /// </summary>
    internal static bool DebugIsPointerHeld() => BreakwaterToolSystem.Instance.IsPointerHeld();

    partial void OnPointerRead(ref bool isHeld) => isHeld = this.pointerHeld ?? isHeld;

    internal static void DebugSetParameter(string name, float value) =>
      BreakwaterToolSystem.Instance.SetParameter(name, value);

    internal static void DebugApplyStyle(string name) =>
      BreakwaterToolSystem.Instance.ApplyStyle(name);

    internal static void DebugReshuffle() => BreakwaterToolSystem.Instance.Reshuffle();

    internal static void DebugClearVegetation() =>
      BreakwaterToolSystem.Instance.ClearPickedVegetation();

    internal static bool DebugAddStone(Entity prefab) =>
      BreakwaterToolSystem.Instance.AddStone(prefab);

    internal static void DebugRemoveStone(Entity prefab) =>
      BreakwaterToolSystem.Instance.RemoveStone(prefab);

    internal static void DebugExpectOffer() => BreakwaterToolSystem.Instance.ExpectOffer();

    /// <summary>
    /// What each part of the tool's update costs, in milliseconds a call, over a number of calls
    /// in a row at the pointer's place: the cull, the ground's verdict, the aim, the overlay, and
    /// the look at the ground's hash; then how many breakwaters are in view.
    /// </summary>
    internal static string DebugTime(int calls = 200) {
      var tool = BreakwaterToolSystem.Instance;
      var target = tool.gestures.Aim(tool.pointer);
      var picked = tool.Picked;
      var at = tool.pointer.At ?? default;
      var ground = new float3(at.x, tool.HeightAt(at), at.y);

      string Time(string part, Action call) {
        var watch = Stopwatch.StartNew();

        for (var i = 0; i < calls; i++) {
          call();
        }

        return $"{part} {watch.Elapsed.TotalMilliseconds / calls:F3}";
      }

      return string.Join(
        ", ",
        Time("cull", tool.CullOutlines),
        Time("toe", tool.JudgeGround),
        Time("ground", tool.JudgePickedGround),
        Time("reach", () => tool.ReachOutlines(ground, tool.pointer.Reach)),
        Time("aim", () => tool.gestures.Aim(tool.pointer)),
        Time("overlay", () => tool.DrawOverlay(target)),
        Time("hash", () => tool.breakwaters.IsOutdated(picked)),
        $"{tool.outlines.Count} at the pointer, {tool.inView.Count} of {tool.city.Count} in view"
      );
    }

    /// <summary>
    /// Lays a grid of small breakwaters around a point, a count along each side and 60 m apart, for
    /// a city of many: each a ring of 12 points, 20 m across.
    /// </summary>
    internal static int DebugMany(float x, float z, int side) {
      var stones = BreakwaterToolSystem.Instance.choice;
      var laid = 0;

      for (var i = 0; i < side; i++) {
        for (var j = 0; j < side; j++) {
          var middle = new float2(x + (i * 60f), z + (j * 60f));

          var ring = Enumerable.Range(0, 12)
            .Select(k =>
              middle + (10f * new float2(math.cos(k * math.PI / 6f), math.sin(k * math.PI / 6f)))
            )
            .ToArray();

          laid += BreakwaterApi.Create(ring, stones).WhyNothing is null ? 1 : 0;
        }
      }

      return laid;
    }

    /// <summary>
    /// Starts a scripted drag of a slider on the picked breakwater, as the panel sends it: a quiet
    /// second, then a set of Irregularity every 100 ms of real time, then a quiet second.
    /// <see cref="DebugBenchResult"/> reads its frames once it ends.
    /// </summary>
    internal static void DebugBench(int sets) =>
      BreakwaterToolSystem.Instance.bench = new DragBench(sets);

    internal static string DebugBenchResult() =>
      BreakwaterToolSystem.Instance.bench?.Result() ?? "no bench";

    partial void OnBenchFrame() => this.bench?.Frame(this);

    internal static void DebugToggle() => BreakwaterToolSystem.Instance.Toggle();

    internal static void DebugPick(Entity record) => BreakwaterToolSystem.Instance.Pick(record);

    internal static void DebugFit() => BreakwaterToolSystem.Instance.FitPicked();

    internal static void DebugRevert() => BreakwaterToolSystem.Instance.RevertPicked();

    internal static void DebugDelete() => BreakwaterToolSystem.Instance.DeletePicked();

    /// <summary>
    /// Where a point of the terrain shows on screen, in pixels from its bottom left corner, as the
    /// input system takes a pointer position; null when it is behind the camera.
    /// Under water, it is where the terrain would show without the water.
    /// </summary>
    internal static Vector2? DebugScreenPoint(float x, float z) {
      var tool = BreakwaterToolSystem.Instance;
      var heights = tool.terrainSystem.GetHeightData(waitForPending: true);
      var y = TerrainUtils.SampleHeight(ref heights, new float3(x, 0f, z));
      var screen = Camera.main.WorldToScreenPoint(new Vector3(x, y, z));

      return screen.z > 0f ? new Vector2(screen.x, screen.y) : null;
    }

    /// <summary>
    /// A scripted drag and the frames it took: before it, during it until its last re-lay settled,
    /// and the layout's own time at each set.
    /// </summary>
    private sealed class DragBench(int sets) {
      private const float Quiet = 1f;

      private const float Interval = 0.1f;

      private readonly List<float> before = [];

      private readonly List<float> during = [];

      private readonly List<double> layouts = [];

      private float start = -1f;

      private int sent;

      private bool isDone;

      internal void Frame(BreakwaterToolSystem tool) {
        var now = UnityEngine.Time.realtimeSinceStartup;

        if (this.isDone) {
          return;
        }

        if (this.start < 0f) {
          this.start = now;

          return;
        }

        var elapsed = now - this.start;
        var frame = UnityEngine.Time.unscaledDeltaTime * 1000f;

        if (elapsed < DragBench.Quiet) {
          this.before.Add(frame);

          return;
        }

        if (elapsed >= DragBench.Quiet + (sets * DragBench.Interval) + DragBench.Quiet) {
          this.isDone = true;

          return;
        }

        this.during.Add(frame);

        if (this.sent < sets && elapsed >= DragBench.Quiet + (this.sent * DragBench.Interval)) {
          var watch = Stopwatch.StartNew();

          // The value the look does not hold, as a set of the value it holds lays nothing.
          tool.SetParameter(
            nameof(BreakwaterParameters.Irregularity),
            tool.Parameters.Irregularity is 0.04f ? 0.03f : 0.04f
          );

          this.layouts.Add(watch.Elapsed.TotalMilliseconds);
          this.sent++;
        }
      }

      /// <summary>
      /// The frames in milliseconds, and the time the re-lays cost beyond the frames before.
      /// </summary>
      internal string Result() {
        if (!this.isDone) {
          return "running";
        }

        var baseline = DragBench.Median(this.before);
        var extra = this.during.Sum() - (baseline * this.during.Count);
        var sorted = this.during.OrderBy(static frame => frame).ToArray();

        var over = this.during.Count(frame => frame > 2f * baseline);

        return string.Join(
          "; ",
          FormattableString.Invariant($"{this.sent} sets, frames before: median {baseline:F1}"),
          FormattableString.Invariant($"during: median {DragBench.Median(this.during):F1}"),
          FormattableString.Invariant($"p95 {sorted[(int) (sorted.Length * 0.95f)]:F1}"),
          FormattableString.Invariant($"max {sorted[^1]:F1}"),
          $"{over} over twice the median",
          FormattableString.Invariant($"extra {extra / this.sent:F0} ms a set"),
          FormattableString.Invariant(
            $"layout mean {this.layouts.Average():F1}, max {this.layouts.Max():F1}"
          )
        );
      }

      private static float Median(List<float> frames) =>
        frames.OrderBy(static frame => frame).ElementAt(frames.Count / 2);
    }
  }
}
#endif
