using System.Linq;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BreakwaterTool.Scenarios.Ground;

/// <summary>
/// The terrain tool's Shift brush, applied from code through the path the tool applies it by.
/// </summary>
internal static class Terraform {
  /// <summary>
  /// The brush's width, in metres, enough to cover a site's outline.
  /// </summary>
  private const float Size = 80f;

  /// <summary>
  /// A stroke's strength, low enough that one stroke raises the ground well under a metre.
  /// </summary>
  private const float Strength = 0.05f;

  /// <summary>
  /// Raises the ground around a point by one stroke of the brush.
  /// A stroke raises it by an amount the frame's time scales, as the tool's own strokes: a caller
  /// measures the rise rather than count strokes.
  /// </summary>
  internal static void Stroke(World world, float2 at) {
    var manager = world.EntityManager;
    var terrain = world.GetExistingSystemManaged<TerrainSystem>();

    using var tools = manager.CreateEntityQuery(
      ComponentType.ReadOnly<TerraformingData>(),
      ComponentType.ReadOnly<PrefabData>()
    );

    using var tool = tools.ToEntityArray(Allocator.Temp);

    var shift = tool.First(entity =>
      manager.GetComponentData<TerraformingData>(entity) is {
        m_Type: TerraformingType.Shift, m_Target: TerraformingTarget.Height
      }
    );

    using var brushes = manager.CreateEntityQuery(
      ComponentType.ReadOnly<BrushData>(),
      ComponentType.ReadOnly<PrefabData>()
    );

    using var brush = brushes.ToEntityArray(Allocator.Temp);

    var texture = world.GetExistingSystemManaged<PrefabSystem>()
      .GetPrefab<BrushPrefab>(brush[0])
      .m_Texture;

    var point = new float3(at.x, Terraform.Height(world, at), at.y);

    var stroke = new Brush {
      m_Tool = shift,
      m_Position = point,
      m_Target = point,
      m_Start = point,
      m_Size = Terraform.Size,
      m_Strength = Terraform.Strength,
      m_Opacity = 1f
    };

    terrain.ApplyBrush(TerraformingType.Shift, ToolUtils.GetBounds(stroke), stroke, texture);
  }

  /// <summary>
  /// The ground's height at a point, as the game's systems read it.
  /// </summary>
  internal static float Height(World world, float2 at) {
    var heights = world.GetExistingSystemManaged<TerrainSystem>()
      .GetHeightData(waitForPending: true);

    return TerrainUtils.SampleHeight(ref heights, new float3(at.x, 0f, at.y));
  }
}
