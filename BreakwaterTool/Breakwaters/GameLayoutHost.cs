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
}
