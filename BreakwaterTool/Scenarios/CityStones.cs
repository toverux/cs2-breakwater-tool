using System;
using System.Collections.Generic;
using System.Linq;
using BreakwaterTool.Breakwaters;
using Colossal.Entities;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BreakwaterTool.Scenarios;

/// <summary>
/// A stone as the scenarios compare it: its prefab by name, which outlives a reload, its transform
/// and its colour seed.
/// </summary>
internal readonly record struct Stone(string Prefab, Transform Transform, ushort Seed);

/// <summary>
/// The scenarios' one check of what the mod laid: the stones linked to a breakwater anywhere in the
/// city, compared bit for bit with the stones each step expects there.
/// The game lays a plan with the arithmetic it plans with, so nothing short of equality passes.
/// </summary>
internal static class CityStones {
  /// <summary>
  /// Every linked stone, by the record its link names, a record that is gone included.
  /// </summary>
  internal static Dictionary<Entity, Stone[]> ByRecord(World world) {
    var manager = world.EntityManager;
    var prefabs = world.GetExistingSystemManaged<PrefabSystem>();

    using var query = manager.CreateEntityQuery(
      ComponentType.ReadOnly<StoneLink>(),
      ComponentType.Exclude<Deleted>(),
      ComponentType.Exclude<Temp>()
    );

    using var stones = query.ToEntityArray(Allocator.Temp);

    return stones.ToArray()
      .GroupBy(stone => manager.GetComponentData<StoneLink>(stone).record)
      .ToDictionary(
        static record => record.Key,
        record => record
          .Select(stone => new Stone(
              prefabs.GetPrefabName(manager.GetComponentData<PrefabRef>(stone).m_Prefab),
              manager.GetComponentData<Transform>(stone),
              manager.TryGetComponent<PseudoRandomSeed>(stone, out var seed)
                ? seed.m_Seed
                : (ushort) 0
            )
          )
          .ToArray()
      );
  }

  /// <summary>
  /// The stones a plan lays, as a check compares them.
  /// </summary>
  internal static Stone[] Named(World world, IEnumerable<StoneState> stones) {
    var prefabs = world.GetExistingSystemManaged<PrefabSystem>();

    return [
      .. stones.Select(stone =>
        new Stone(prefabs.GetPrefabName(stone.Prefab), stone.Transform, stone.Seed)
      )
    ];
  }

  /// <summary>
  /// How many linked stones the game hides, as it hides a prop it finds overlapping another: in the
  /// city, or of one breakwater.
  /// </summary>
  internal static int Hidden(World world, Entity record = default) {
    using var query = world.EntityManager.CreateEntityQuery(
      ComponentType.ReadOnly<StoneLink>(),
      ComponentType.ReadOnly<Overridden>(),
      ComponentType.Exclude<Deleted>(),
      ComponentType.Exclude<Temp>()
    );

    if (record == Entity.Null) {
      return query.CalculateEntityCount();
    }

    using var links = query.ToComponentDataArray<StoneLink>(Allocator.Temp);

    return links.Count(link => link.record == record);
  }

  internal static StoneCheck Compare(IEnumerable<Stone> expected, IEnumerable<Stone> actual) {
    // Each stone's expected count less its actual count: a multiset difference in one pass.
    var tally = new Dictionary<Stone, int>();
    var expectedCount = 0;
    var actualCount = 0;

    foreach (var stone in expected) {
      tally[stone] = (tally.TryGetValue(stone, out var count) ? count : 0) + 1;
      expectedCount++;
    }

    foreach (var stone in actual) {
      tally[stone] = (tally.TryGetValue(stone, out var count) ? count : 0) - 1;
      actualCount++;
    }

    return new StoneCheck(
      expectedCount,
      actualCount,
      [.. tally.Where(static stone => stone.Value > 0).Select(static stone => stone.Key)],
      [.. tally.Where(static stone => stone.Value < 0).Select(static stone => stone.Key)]
    );
  }
}

/// <param name="Missing">Expected stones the city lacks, each once.</param>
/// <param name="Extra">Stones the city holds beyond those expected, each once.</param>
internal sealed record StoneCheck(
  int Expected,
  int Actual,
  IReadOnlyList<Stone> Missing,
  IReadOnlyList<Stone> Extra
) {
  internal bool Holds => this.Missing.Count == 0 && this.Extra.Count == 0;

  /// <summary>
  /// The outcome in one line; where a stone is missing, the nearest extra stone tells a stone
  /// moved by metres from one off by a rounding.
  /// </summary>
  public override string ToString() {
    var counts = FormattableString.Invariant($"{this.Actual} stones for {this.Expected} expected");

    if (this.Holds) {
      return counts;
    }

    var text = $"{counts}, {this.Missing.Count} missing, {this.Extra.Count} extra";

    if (this.Missing.Count == 0) {
      return text;
    }

    var first = this.Missing[0];
    var position = first.Transform.m_Position;

    text += FormattableString.Invariant(
      $"; first missing {first.Prefab} at ({position.x:F2}, {position.y:F2}, {position.z:F2})"
    );

    if (this.Extra.Count == 0) {
      return text;
    }

    var nearest = this.Extra
      .OrderBy(stone => math.distance(stone.Transform.m_Position, position))
      .First();

    return text +
      FormattableString.Invariant(
        $", nearest extra {nearest.Prefab} {math.distance(nearest.Transform.m_Position, position):G4} m away"
      );
  }
}
