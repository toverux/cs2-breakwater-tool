using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BreakwaterTool.Layout;

/// <summary>
/// The distance to the polygon's toe on a grid, positive inside the polygon, read anywhere in game
/// metres by bilinear interpolation. The rows climb it: row t is the contour at distance t.
/// </summary>
internal sealed class ToeField {
  /// <summary>
  /// The most cells the field holds, 16 MB of them: an outline drawn long on the diagonal spans a
  /// vast bounding box, which a field at the requested step would fill with hundreds of megabytes.
  /// </summary>
  internal const int Cap = 4_000_000;

  private readonly float[] distances;

  private readonly float2 origin;

  private readonly float cell;

  private readonly int countX;

  private readonly int countZ;

  /// <param name="polygon">The breakwater's outline.</param>
  /// <param name="toe">Per edge of the outline, whether it belongs to the toe.</param>
  /// <param name="cell">
  /// The grid's step, in metres, made coarser where the outline's bounds would hold more than
  /// <see cref="Cap"/> cells.
  /// </param>
  /// <param name="margin">How far the grid reaches past the outline's bounds, in metres.</param>
  /// <param name="host">Fills the grid.</param>
  internal ToeField(
    IReadOnlyList<float2> polygon,
    IReadOnlyList<bool> toe,
    float cell,
    float margin,
    ILayoutHost host
  ) {
    var min = polygon[0];
    var max = polygon[0];

    foreach (var point in polygon) {
      min = math.min(min, point);
      max = math.max(max, point);
    }

    var span = max - min + (2f * margin);

    cell = math.max(cell, math.sqrt(span.x * span.y / ToeField.Cap));

    // The cells past the bounds' edges can still put the count over the cap.
    while ((long) ToeField.Count(span.x, cell) * ToeField.Count(span.y, cell) > ToeField.Cap) {
      cell *= 1.05f;
    }

    this.origin = min - margin;
    this.cell = cell;
    this.countX = ToeField.Count(span.x, cell);
    this.countZ = ToeField.Count(span.y, cell);

    using var points = new NativeArray<float2>(polygon.ToArray(), Allocator.TempJob);
    using var toeEdges = new NativeArray<bool>(toe.ToArray(), Allocator.TempJob);
    using var grid = new NativeArray<float>(this.countX * this.countZ, Allocator.TempJob);

    host.Fill(
      new ToeDistanceJob {
        polygon = points,
        toe = toeEdges,
        origin = this.origin,
        cell = cell,
        countX = this.countX,
        distances = grid
      }
    );

    this.distances = grid.ToArray();
  }

  internal int CellCount => this.distances.Length;

  private static int Count(float span, float cell) => (int) math.ceil(span / cell) + 1;

  internal float Sample(float2 point) {
    var grid = math.clamp(
      (point - this.origin) / this.cell,
      0f,
      new float2(this.countX - 1.001f, this.countZ - 1.001f)
    );

    var i = (int) grid.x;
    var j = (int) grid.y;
    var fraction = grid - new float2(i, j);

    var below = math.lerp(
      this.distances[(j * this.countX) + i],
      this.distances[(j * this.countX) + i + 1],
      fraction.x
    );

    var above = math.lerp(
      this.distances[((j + 1) * this.countX) + i],
      this.distances[((j + 1) * this.countX) + i + 1],
      fraction.x
    );

    return math.lerp(below, above, fraction.y);
  }

  /// <summary>
  /// The field's unit gradient at a point, pointing away from the toe, up the slope.
  /// </summary>
  internal float2 Gradient(float2 point) {
    var step = this.cell;

    return math.normalizesafe(
      new float2(
        this.Sample(point + new float2(step, 0f)) - this.Sample(point - new float2(step, 0f)),
        this.Sample(point + new float2(0f, step)) - this.Sample(point - new float2(0f, step))
      )
    );
  }

  /// <summary>
  /// A point moved along the gradient onto the contour at distance t, or null when it cannot get
  /// near it: out of the polygon, or past a jetty's spine, where the field folds.
  /// </summary>
  /// <param name="tolerance">
  /// How far from the contour a point may settle, in row spacings: 0.6 lets the last row of a jetty
  /// settle on its spine, closing the seam between its two sides.
  /// </param>
  internal float2? Move(float2 point, float t, float rowSpacing, float tolerance = 0.6f) {
    for (var k = 0; k < 3; k++) {
      point += this.Gradient(point) * (t - this.Sample(point));
    }

    return Math.Abs(this.Sample(point) - t) < tolerance * rowSpacing ? point : null;
  }
}

/// <summary>
/// Writes one cell of a <see cref="ToeField"/>: its signed distance to the toe edges.
/// </summary>
[BurstCompile]
internal struct ToeDistanceJob : IJobParallelFor {
  [ReadOnly]
  public NativeArray<float2> polygon;

  [ReadOnly]
  public NativeArray<bool> toe;

  public float2 origin;

  public float cell;

  public int countX;

  [WriteOnly]
  public NativeArray<float> distances;

  public void Execute(int index) {
    var point = this.origin + (new float2(index % this.countX, index / this.countX) * this.cell);
    var nearest = float.MaxValue;
    var inside = false;

    for (var l = 0; l < this.polygon.Length; l++) {
      var a = this.polygon[l];
      var b = this.polygon[(l + 1) % this.polygon.Length];
      var ab = b - a;

      if (this.toe[l]) {
        var u = math.saturate(math.dot(point - a, ab) / math.max(math.lengthsq(ab), 1e-6f));

        nearest = math.min(nearest, math.lengthsq(point - a - (u * ab)));
      }

      // Even-odd crossing count of a ray from the point towards +x.
      if ((a.y > point.y) != (b.y > point.y) &&
        point.x < a.x + ((point.y - a.y) / (b.y - a.y) * ab.x)) {
        inside = !inside;
      }
    }

    this.distances[index] = inside ? math.sqrt(nearest) : -math.sqrt(nearest);
  }
}
