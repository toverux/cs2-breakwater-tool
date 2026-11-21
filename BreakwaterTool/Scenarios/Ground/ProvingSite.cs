using System;
using BreakwaterTool.Layout;
using Unity.Mathematics;

namespace BreakwaterTool.Scenarios.Ground;

/// <summary>
/// A breakwater of the proving ground: its plot of relief, its outline on it, its stones, and the
/// verdict the layout must reach there. Off-engine tests and in-game scenarios lay the same sites.
/// </summary>
internal sealed class ProvingSite {
  internal required string Name { get; init; }

  /// <summary>
  /// What the site puts to the test, in a few words.
  /// </summary>
  internal required string Shows { get; init; }

  /// <summary>
  /// The plot's first post along x and z.
  /// </summary>
  internal required int2 FirstPost { get; init; }

  /// <summary>
  /// The plot's size, in posts.
  /// </summary>
  internal required int2 Posts { get; init; }

  /// <summary>
  /// The ground's height at a post of the plot, as the heightmap stores it.
  /// </summary>
  internal required Func<int2, ushort> Height { get; init; }

  /// <summary>
  /// The breakwater's outline, in game metres.
  /// </summary>
  internal required float2[] Outline { get; init; }

  internal required SiteStones Stones { get; init; }

  internal BreakwaterParameters Parameters { get; init; } = BreakwaterParameters.Default;

  internal LayoutVerdict Verdict { get; init; } = LayoutVerdict.Laid;

  public override string ToString() => this.Name;
}

/// <summary>
/// The stones a site lays: off-engine the boxes of <see cref="FixtureStones"/>, in the game the
/// prefabs they stand for.
/// </summary>
internal enum SiteStones {
  /// <summary>
  /// The eight boulders of the vanilla <c>BoulderRandom01</c>, about 4.5 m across.
  /// </summary>
  Boulders,

  /// <summary>
  /// The vanilla <c>CratePile01</c>, 2 m by 1.2 m, for sites that need many stones.
  /// </summary>
  Crates
}
