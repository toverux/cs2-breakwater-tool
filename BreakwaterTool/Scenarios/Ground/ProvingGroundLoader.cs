using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace BreakwaterTool.Scenarios.Ground;

/// <summary>
/// Puts the <see cref="ProvingGround"/> under the loaded map, as the editor imports a heightmap: a
/// scenario needs no map of its own, as any new map becomes the ground it expects.
/// </summary>
internal static class ProvingGroundLoader {
  internal static void Apply(World world) {
    var terrain = world.GetOrCreateSystemManaged<TerrainSystem>();

    var map = new Texture2D(
      ProvingGround.Resolution,
      ProvingGround.Resolution,
      GraphicsFormat.R16_UNorm,
      TextureCreationFlags.None
    );

    map.SetPixelData(ProvingGround.Heights(), 0);
    map.Apply(false);

    // The height scale first: the game reads the heightmap against it.
    terrain.SetTerrainProperties(new float2(ProvingGround.HeightScale, 0f));
    terrain.ReplaceHeightmap(map);

    // The game keeps a copy.
    Object.Destroy(map);
  }

  /// <summary>
  /// Whether the heights the game's systems read are the proving ground's, value for value.
  /// </summary>
  internal static bool IsLoaded(World world) {
    var terrain = world.GetExistingSystemManaged<TerrainSystem>();

    using var expected = new NativeArray<ushort>(ProvingGround.Heights(), Allocator.Persistent);

    return ProvingGround.Digest(terrain.GetHeightData(waitForPending: true).heights) ==
      ProvingGround.Digest(expected);
  }

  /// <summary>
  /// Whether the game maps the world onto the heights exactly as the off-engine fixtures do: the
  /// same posts under the same metres, so that their layouts sample the game's ground.
  /// </summary>
  internal static bool IsMapped(World world) {
    var game = world.GetExistingSystemManaged<TerrainSystem>().GetHeightData(waitForPending: true);
    var fixture = ProvingGround.HeightData(game.heights);

    return game.resolution.Equals(fixture.resolution) &&
      game.scale.Equals(fixture.scale) &&
      game.offset.Equals(fixture.offset);
  }
}
