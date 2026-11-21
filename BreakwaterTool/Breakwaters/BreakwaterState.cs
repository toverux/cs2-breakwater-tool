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
internal sealed record BreakwaterState(
  float2[] Polygon,
  Entity[] Choice,
  BreakwaterParameters Parameters,
  StoneProtectionKind Protection
);

/// <summary>
/// A stone as laid: enough to lay it again exactly.
/// </summary>
/// <param name="Seed">Its <c>PseudoRandomSeed</c>, which picks its colour variation.</param>
internal readonly record struct StoneState(Entity Prefab, Transform Transform, ushort Seed);

/// <summary>
/// A breakwater as it stood when picked, which Revert restores.
/// </summary>
internal sealed record BreakwaterSnapshot(
  Entity Record,
  BreakwaterState State,
  StoneState[] Stones
);
