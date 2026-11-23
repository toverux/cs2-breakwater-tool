using System.Collections.Generic;
using BreakwaterTool.Layout;
using Game.Simulation;
using Unity.Jobs;
using Unity.Mathematics;

namespace BreakwaterTool.Breakwaters;

/// <summary>
/// The layout's host in the game: the terrain's heights, and the distance field filled on Burst.
/// </summary>
internal sealed class GameLayoutHost(TerrainHeightData terrain) : ILayoutHost {
  // A struct the sampler takes by reference, hence a field of its own and not readonly.
  private TerrainHeightData heights = terrain;

  public float HeightAt(float2 xz) =>
    TerrainUtils.SampleHeight(ref this.heights, new float3(xz.x, 0f, xz.y));

  public void Fill(ToeDistanceJob job) => job.Schedule(job.distances.Length, 64).Complete();

  /// <summary>
  /// The hash of the ground under an outline: of the cells of the terrain's heightmap its bounds
  /// cover, and of the backdrop's, which holds the ground past the playable area.
  /// </summary>
  internal ulong GroundHashOf(IReadOnlyList<float2> polygon) {
    var hash = GroundHash.Add(
      GroundHash.Start,
      this.heights.heights,
      this.heights.resolution.x,
      GroundHash.Cells(
        polygon,
        xz => TerrainUtils.ToHeightmapSpace(ref this.heights, new float3(xz.x, 0f, xz.y)).xz,
        this.heights.resolution.xz
      )
    );

    if (!this.heights.hasBackdrop || this.heights.downscaledHeights.Length == 0) {
      return hash;
    }

    return GroundHash.Add(
      hash,
      this.heights.downscaledHeights,
      this.heights.downScaledResolution.x,
      GroundHash.Cells(
        polygon,
        xz => TerrainUtils.ToBackdropSpace(ref this.heights, new float3(xz.x, 0f, xz.y)).xz,
        this.heights.downScaledResolution.xz
      )
    );
  }
}
