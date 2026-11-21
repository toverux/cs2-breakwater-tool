#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using BreakwaterTool.Gestures;
using Game.Input;
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

    internal static BreakwaterToolSystem Instance =>
      World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BreakwaterToolSystem>();

    /// <summary>
    /// The tool's state on one line: active, mode, points, the vertex held, the pick, the hint, the
    /// refusal and the ground.
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
        $"ground {tool.Ground}, " +
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
  }
}
#endif
