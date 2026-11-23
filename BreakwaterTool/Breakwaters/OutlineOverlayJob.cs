using Colossal.Mathematics;
using Game.Rendering;
using Game.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace BreakwaterTool.Breakwaters;

/// <summary>
/// Draws the outlines of the breakwaters in view, each an edge straight from node to node on the
/// ground, in its own colour.
/// </summary>
// [PERF] On Burst, as the game draws its areas, in place of a managed loop over every edge in
// view at each frame.
[BurstCompile]
internal struct OutlineOverlayJob : IJob {
  /// <summary>
  /// Every outline's points, one outline after the other.
  /// </summary>
  [ReadOnly]
  public NativeArray<float2> points;

  /// <summary>
  /// Per outline, the index past its last point.
  /// </summary>
  [ReadOnly]
  public NativeArray<int> ends;

  [ReadOnly]
  public NativeArray<Color> colors;

  [ReadOnly]
  public TerrainHeightData heights;

  public OverlayRenderSystem.Buffer buffer;

  /// <summary>
  /// Where the camera is.
  /// </summary>
  public float3 eye;

  /// <summary>
  /// An edge's width near the camera, in metres.
  /// </summary>
  public float width;

  /// <summary>
  /// An edge's least width per metre from the camera, which keeps it visible from afar.
  /// </summary>
  public float widthPerMetre;

  public void Execute() {
    var start = 0;

    for (var outline = 0; outline < this.ends.Length; outline++) {
      var end = this.ends[outline];

      if (end > start) {
        var from = this.Lift(this.points[end - 1]);

        for (var i = start; i < end; i++) {
          var to = this.Lift(this.points[i]);
          var edge = new Line3.Segment(from, to);
          var away = math.distance(this.eye, MathUtils.Position(edge, 0.5f));

          this.buffer.DrawLine(
            this.colors[outline],
            this.colors[outline],
            0f,
            0,
            edge,
            math.max(this.width, this.widthPerMetre * away),
            new float2(1f, 1f)
          );

          from = to;
        }
      }

      start = end;
    }
  }

  private float3 Lift(float2 xz) =>
    new(xz.x, TerrainUtils.SampleHeight(ref this.heights, new float3(xz.x, 0f, xz.y)), xz.y);
}
