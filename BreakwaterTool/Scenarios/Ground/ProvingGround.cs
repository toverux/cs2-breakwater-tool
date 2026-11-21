using System;
using Game.Simulation;
using Unity.Collections;
using Unity.Mathematics;

namespace BreakwaterTool.Scenarios.Ground;

/// <summary>
/// A terrain made for tests: a straight coast along x through the middle of the map, land towards
/// +z and sea towards -z, and one plot of relief per site of <see cref="ProvingSites"/> along it.
/// The game loads it under a new map, and off-engine tests sample the same heights the same way, so
/// a site lays the same stones in both.
/// Its heights are computed in double precision from sums, products, quotients and square roots
/// only, which round alike on CoreCLR, on the game's Mono and in both float passes.
/// </summary>
internal static class ProvingGround {
  /// <summary>
  /// The heightmap's posts along x and along z, as the game's.
  /// </summary>
  internal const int Resolution = 4096;

  /// <summary>
  /// The distance between two posts, in metres: the playable area's 14 336 m over the resolution.
  /// </summary>
  internal const double CellSize = 3.5;

  /// <summary>
  /// The height that a post's largest value stands for, in metres.
  /// </summary>
  internal const float HeightScale = 256f;

  /// <summary>
  /// The sea's level, high enough over zero to hold the sea floor of the Nice patches.
  /// </summary>
  internal const double SeaLevel = 80;

  internal const double Land = 82;

  internal const double SeaFloor = 70;

  /// <summary>
  /// Where a post lies along x or z, in metres: the game puts a post at the middle of its cell.
  /// </summary>
  internal static double World(int post) =>
    (post + 0.5 - (ProvingGround.Resolution / 2)) * ProvingGround.CellSize;

  /// <summary>
  /// A height as the heightmap stores it.
  /// </summary>
  internal static ushort Encode(double height) =>
    (ushort) Math.Round(
      Math.Min(Math.Max(height / ProvingGround.HeightScale * ushort.MaxValue, 0), ushort.MaxValue)
    );

  /// <summary>
  /// The coast's profile across it, at z metres from the shore line: the land, a bank falling one
  /// in two that meets the sea 4 m out, then the sea floor.
  /// </summary>
  internal static double Coast(double z) =>
    Math.Min(Math.Max(ProvingGround.Land + (0.5 * z), ProvingGround.SeaFloor), ProvingGround.Land);

  /// <summary>
  /// The whole heightmap, row after row of growing z.
  /// </summary>
  internal static ushort[] Heights() {
    var heights = new ushort[ProvingGround.Resolution * ProvingGround.Resolution];

    for (var z = 0; z < ProvingGround.Resolution; z++) {
      var coast = ProvingGround.Encode(ProvingGround.Coast(ProvingGround.World(z)));

      for (var x = 0; x < ProvingGround.Resolution; x++) {
        heights[(z * ProvingGround.Resolution) + x] = coast;
      }
    }

    foreach (var site in ProvingSites.All) {
      for (var z = 0; z < site.Posts.y; z++) {
        for (var x = 0; x < site.Posts.x; x++) {
          var post = site.FirstPost + new int2(x, z);

          heights[(post.y * ProvingGround.Resolution) + post.x] = site.Height(post);
        }
      }
    }

    return heights;
  }

  /// <summary>
  /// A digest of the heightmap, FNV-1a over its values: equal digests are the same ground.
  /// </summary>
  internal static uint Digest(NativeArray<ushort> heights) {
    var hash = 2166136261u;

    foreach (var height in heights) {
      hash = (hash ^ height) * 16777619u;
    }

    return hash;
  }

  /// <summary>
  /// The height data the game gives its systems once this ground is loaded, built as
  /// <see cref="TerrainSystem.GetHeightData"/> builds it, for <see cref="TerrainUtils"/> to sample.
  /// </summary>
  internal static TerrainHeightData HeightData(NativeArray<ushort> heights) {
    var resolution = new int3(ProvingGround.Resolution, 65536, ProvingGround.Resolution);
    var size = new float3(14336f, ProvingGround.HeightScale, 14336f);
    var scale = new float3(resolution.x, resolution.y - 1, resolution.z) / size;

    // The game's position offset is minus half the playable area, and zero in height here.
    var offset = new float3(7168f, 0f, 7168f);

    offset.xz -= 0.5f / scale.xz;

    return new TerrainHeightData(heights, default, resolution, scale, offset, false);
  }
}
