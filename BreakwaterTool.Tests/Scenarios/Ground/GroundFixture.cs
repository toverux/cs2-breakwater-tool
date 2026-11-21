using System;
using System.Collections.Concurrent;
using BreakwaterTool.Layout;
using BreakwaterTool.Scenarios.Ground;
using BreakwaterTool.Tests.Layout;
using Game.Simulation;
using Unity.Collections;
using Unity.Mathematics;

namespace BreakwaterTool.Tests.Scenarios.Ground;

/// <summary>
/// The proving ground's heights, built once per test run, and each site's layout on them.
/// </summary>
internal static class GroundFixture {
  private static readonly Lazy<NativeArray<ushort>> LazyHeights = new(static () =>
    new NativeArray<ushort>(ProvingGround.Heights(), Allocator.Persistent)
  );

  private static readonly ConcurrentDictionary<string, LayoutResult> Layouts = new();

  internal static NativeArray<ushort> Heights => GroundFixture.LazyHeights.Value;

  /// <summary>
  /// The layout's host over the ground, sampled as the game samples its terrain.
  /// </summary>
  internal static FixtureHost Host {
    get {
      var heights = ProvingGround.HeightData(GroundFixture.Heights);

      return new FixtureHost(xz =>
        TerrainUtils.SampleHeight(ref heights, new float3(xz.x, 0f, xz.y))
      );
    }
  }

  /// <summary>
  /// The site's layout with its fixture stones, laid once and shared by the tests that read it.
  /// </summary>
  internal static LayoutResult Layout(ProvingSite site) =>
    GroundFixture.Layouts.GetOrAdd(site.Name, _ => GroundFixture.Lay(site));

  /// <summary>
  /// The site's layout with its fixture stones, laid anew.
  /// </summary>
  internal static LayoutResult Lay(ProvingSite site) =>
    BreakwaterLayout.Lay(
      site.Outline,
      FixtureStones.For(site.Stones),
      site.Parameters,
      GroundFixture.Host
    );
}
