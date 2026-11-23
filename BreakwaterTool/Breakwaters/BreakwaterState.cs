using System.Collections.Generic;
using Game.Objects;
using Unity.Entities;
using Unity.Mathematics;

namespace BreakwaterTool.Breakwaters;

/// <summary>
/// What a breakwater's record holds.
/// </summary>
/// <param name="Polygon">The outline, in game metres seen from above, in drawing order.</param>
/// <param name="Choice">
/// The chosen prefabs: stones, and random prefabs whose stones are laid.
/// </param>
/// <param name="Toe">
/// Per edge of the outline, from its point i to i + 1, whether it is toe, picked by hand; null
/// where the layout chooses the toe.
/// </param>
/// <param name="GroundHash">The hash of the ground the stones were laid on.</param>
internal sealed record BreakwaterState(
  float2[] Polygon,
  Entity[] Choice,
  BreakwaterParameters Parameters,
  StoneProtectionKind Protection,
  bool[]? Toe = null,
  ulong GroundHash = 0
);

/// <summary>
/// A plant as it stood before it was cleared from a breakwater: enough to lay it again.
/// </summary>
/// <param name="Tree">Its age and growth, null for a plant that is no tree.</param>
/// <param name="IsNative">Whether it came with the map.</param>
internal readonly record struct PlantState(
  Entity Prefab,
  Transform Transform,
  ushort Seed,
  Tree? Tree,
  bool IsNative
);

/// <summary>
/// A stone as laid: enough to lay it again exactly.
/// </summary>
/// <param name="Seed">Its <c>PseudoRandomSeed</c>, which picks its colour variation.</param>
internal readonly record struct StoneState(Entity Prefab, Transform Transform, ushort Seed);

/// <summary>
/// A breakwater as it stood when picked, which Revert restores, with the plants cleared from it
/// since.
/// </summary>
internal sealed record BreakwaterSnapshot(
  Entity Record,
  BreakwaterState State,
  StoneState[] Stones
) {
  internal List<PlantState> Cleared { get; } = [];
}
