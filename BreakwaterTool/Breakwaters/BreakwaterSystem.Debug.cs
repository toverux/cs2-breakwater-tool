#if DEBUG
using System;
using System.Globalization;
using System.Linq;
using BreakwaterTool.Layout;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BreakwaterTool.Breakwaters {
  /// <summary>
  /// Debug entry points for the unity eval, for what <see cref="BreakwaterApi"/> does not show.
  /// </summary>
  internal sealed partial class BreakwaterSystem {
    // The eval's snapshot: one at a time, as the tool keeps one per picked breakwater.
    private static BreakwaterSnapshot? debugSnapshot;

    /// <summary>
    /// Anarchy's editor option, null when it cannot be read; set for the scenarios.
    /// </summary>
    internal static bool? DebugEditorOption {
      get => BreakwaterSystem.Instance.protection.Link?.PreventOverrideInEditor;
      set {
        if (BreakwaterSystem.Instance.protection.Link is {} bridge) {
          bridge.PreventOverrideInEditor = value;
        }
      }
    }

    private static BreakwaterSystem Instance =>
      World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BreakwaterSystem>();

    /// <summary>
    /// Prefab entities by name, comma-separated, each <c>Type:Name</c> or a static object's name,
    /// e.g. <c>BreakwaterTool.Breakwaters.BreakwaterSystem.DebugPrefabs("Boulder01,Boulder02")</c>.
    /// A name not found gives <see cref="Entity.Null"/>.
    /// </summary>
    internal static Entity[] DebugPrefabs(string names) {
      var prefabs = BreakwaterSystem.Instance.prefabSystem;

      return [
        .. names.Split(',')
          .Select(static name => name.Trim().Split(':'))
          .Select(static parts => parts.Length == 2
            ? new PrefabID(parts[0], parts[1])
            : new PrefabID(nameof(StaticObjectPrefab), parts[0])
          )
          .Select(id => prefabs.TryGetPrefab(id, out var found)
            ? prefabs.GetEntity(found)
            : Entity.Null
          )
      ];
    }

    /// <summary>
    /// A polygon for <see cref="BreakwaterApi"/>, which the eval cannot build: its points as
    /// <c>x,z</c> in game metres, separated by spaces, e.g.
    /// <c>BreakwaterTool.Breakwaters.BreakwaterSystem.DebugPolygon("0,0 40,0 40,10 0,10")</c>.
    /// </summary>
    internal static float2[] DebugPolygon(string points) => [
      .. points.Split([' '], StringSplitOptions.RemoveEmptyEntries)
        .Select(static point => point.Split(','))
        .Select(static xz => new float2(
            float.Parse(xz[0], CultureInfo.InvariantCulture),
            float.Parse(xz[1], CultureInfo.InvariantCulture)
          )
        )
    ];

    /// <summary>
    /// The last layout's diagnostics, the work waiting for the next update, and the last failure.
    /// </summary>
    internal static string DebugLastLayout() {
      var system = BreakwaterSystem.Instance;
      var layout = system.LastLayout;

      var unprotectable = string.Join(
        ", ",
        system.LastUnprotectable.Select(system.catalog.Name)
      );

      return layout is null
        ? $"no layout yet, {system.pending.Count} waiting, last failure: {system.LastFailure}"
        : $"{layout.Verdict} ({layout.WhyNothing}), {layout.Stones.Count} stones of about " +
        $"{layout.Estimate}, {layout.Rows} rows, spacing {layout.StoneSpacing:F2} along, " +
        $"{layout.RowSpacing:F2} across, toe edges " +
        $"[{string.Join("", layout.ToeEdges.Select(static edge => edge ? '1' : '0'))}], " +
        $"left out by size [{string.Join(", ", layout.LeftOut)}], " +
        $"unprotectable [{unprotectable}], {system.pending.Count} waiting, " +
        $"last failure: {system.LastFailure}";
    }

    /// <summary>
    /// How a breakwater's stones stand: protected, locked, overridden by the game, hidden.
    /// </summary>
    internal static string DebugStones(Entity record) {
      var system = BreakwaterSystem.Instance;
      var stones = system.Stones(record);
      var manager = system.EntityManager;
      var bridge = system.protection.Link;

      int Count(ComponentType type) => stones.Count(stone => manager.HasComponent(stone, type));

      var protectedCount = bridge is null ? -1 : Count(bridge.OverridePreventionType);
      var lockedCount = bridge is null ? -1 : Count(bridge.TransformLockType);

      return $"{stones.Count} stones, {protectedCount} protected, {lockedCount} locked, " +
        $"{Count(ComponentType.ReadOnly<Overridden>())} overridden, " +
        $"{Count(ComponentType.ReadOnly<Hidden>())} hidden";
    }

    /// <summary>
    /// Takes the snapshot <see cref="DebugRevert"/> restores.
    /// </summary>
    internal static string DebugSnapshot(Entity record) {
      BreakwaterSystem.debugSnapshot = BreakwaterSystem.Instance.Snapshot(record);

      return BreakwaterSystem.debugSnapshot is {} snapshot
        ? $"snapshot of {record}, {snapshot.Stones.Length} stones"
        : $"{record} is not a breakwater";
    }

    /// <summary>
    /// Reverts the breakwater <see cref="DebugSnapshot"/> took to its snapshot.
    /// </summary>
    /// <returns>Why nothing is restored, or null.</returns>
    internal static string? DebugRevert() =>
      BreakwaterSystem.debugSnapshot is {} snapshot
        ? BreakwaterSystem.Instance.Revert(snapshot)
        : "no snapshot is taken";

    /// <summary>
    /// A breakwater laid out again on the city's terrain, as a fit would lay it, without touching
    /// the city: what the scenarios expect the city to hold.
    /// </summary>
    /// <returns>The stones, or null when nothing would be laid.</returns>
    internal static StoneState[]? DebugPlan(Entity record) {
      var system = BreakwaterSystem.Instance;

      return system.Read(record) is {} state && system.Plan(ref state, out var stones) is null
        ? stones
        : null;
    }

    /// <summary>
    /// The shapes the catalog gives the stones of a choice, by prefab name: what the off-engine
    /// fixture stones stand for.
    /// </summary>
    internal static (string Name, StoneShape Shape)[] DebugShapes(string names) {
      var catalog = BreakwaterSystem.Instance.catalog;

      return [
        .. catalog.Expand(BreakwaterSystem.DebugPrefabs(names))
          .Select(prefab => (catalog.Name(prefab), catalog.Shape(prefab)))
      ];
    }

    /// <summary>
    /// Every prefab the catalog takes for a stone, by what laying it takes: each class with its
    /// count and its first names.
    /// </summary>
    internal static string DebugCares(int names = 30) {
      var system = BreakwaterSystem.Instance;

      using var query = system.EntityManager.CreateEntityQuery(
        ComponentType.ReadOnly<ObjectGeometryData>(),
        ComponentType.ReadOnly<PrefabData>()
      );

      using var prefabs = query.ToEntityArray(Allocator.Temp);

      return string.Join(
        "; ",
        prefabs.ToArray()
          .Where(system.catalog.IsStone)
          .GroupBy(prefab => system.protection.Classify(system.EntityManager, prefab))
          .Select(group =>
            $"{group.Key} {group.Count()}: " +
            string.Join(", ", group.Take(names).Select(system.catalog.Name))
          )
      );
    }

    /// <summary>
    /// The plants Clear vegetation reaches within a distance of a point, and the nearest one's
    /// place.
    /// </summary>
    internal static string DebugPlants(float x, float z, float radius) {
      var system = BreakwaterSystem.Instance;

      using var transforms =
        system.plantsQuery.ToComponentDataArray<Transform>(Allocator.Temp);

      var at = new float2(x, z);

      var near = transforms.ToArray()
        .Select(transform => transform.m_Position.xz)
        .OrderBy(plant => math.distance(plant, at))
        .ToArray();

      return $"{near.Count(plant => math.distance(plant, at) <= radius)} of {near.Length} plants " +
        $"within {radius} m, the nearest at {near.FirstOrDefault()}";
    }

    /// <summary>
    /// Anarchy's bridge as the protection sees it, and its editor option.
    /// </summary>
    internal static string DebugProtection() {
      var protection = BreakwaterSystem.Instance.protection;

      return protection.Link is {} bridge
        ? $"Anarchy {bridge.Version} linked, editor option {bridge.PreventOverrideInEditor}"
        : $"unavailable: {protection.Unavailable}";
    }
  }
}
#endif
