using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace BreakwaterTool.Layout;

/// <summary>
/// The distance to the polygon's toe on a grid, inside the polygon and out. The rows climb it: row
/// t is the contour at distance t, clipped to the polygon.
/// </summary>
internal sealed partial class ToeField {
  /// <summary>
  /// The most cells the field holds: an outline drawn long on the diagonal spans a vast bounding
  /// box, which a field at the requested step would fill with hundreds of megabytes.
  /// </summary>
  internal const int Cap = 4_000_000;

  /// <summary>
  /// How far the grid reaches past the outline's bounds, in cells: enough that a cell inside the
  /// outline has others on every side, which the contours read unchecked.
  /// A read of the field farther out takes the grid's edge.
  /// </summary>
  private const int Margin = 3;

  /// <summary>
  /// The gradient's length under which the field folds: two sides of the breakwater face each other
  /// there, along its spine.
  /// </summary>
  private const float FoldGradient = 0.6f;

  // [PERF] An array, in place of the list given, whose items Mono reads through an interface.
  private readonly float2[] outline;

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
  /// <param name="host">Fills the grid.</param>
  internal ToeField(
    IReadOnlyList<float2> polygon,
    IReadOnlyList<bool> toe,
    float cell,
    ILayoutHost host
  ) {
    var min = polygon[0];
    var max = polygon[0];

    foreach (var point in polygon) {
      min = math.min(min, point);
      max = math.max(max, point);
    }

    var span = max - min;

    cell = math.max(cell, math.sqrt(span.x * span.y / ToeField.Cap));

    // The cells past the bounds' edges can still put the count over the cap.
    while ((long) ToeField.Count(span.x, cell) * ToeField.Count(span.y, cell) > ToeField.Cap) {
      cell *= 1.05f;
    }

    this.outline = polygon.ToArray();
    this.origin = min - (ToeField.Margin * cell);
    this.cell = cell;
    this.countX = ToeField.Count(span.x, cell);
    this.countZ = ToeField.Count(span.y, cell);

    using var points = new NativeArray<float2>(this.outline, Allocator.TempJob);
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
    this.SortCells();
  }

  internal int CellCount => this.distances.Length;

  /// <summary>
  /// The lowest level whose line is whole. Nodes on both sides of the toe can be as far from it as
  /// half a cell's diagonal, and a lower level's line breaks between them.
  /// </summary>
  internal float LowestWhole => 0.75f * this.cell;

  /// <summary>
  /// The point of the spine uphill of a point, or null when none is within a reach, or the outline
  /// ends before it.
  /// </summary>
  internal float2? Climb(float2 from, float reach) {
    // The way up a side is straight, as far as the fold.
    var up = this.Up(from);

    // [PERF] A side that rises all the way to the reach is refused at once, in place of climbed:
    // most places are nowhere near the spine.
    if (this.Sample(from + (up * reach)) > this.Sample(from) + reach - (0.05f * this.cell)) {
      return null;
    }

    for (var climb = 0f; climb < reach; climb += 0.5f * this.cell) {
      var point = from + (up * climb);

      if (math.length(this.Slope(point, 2f * this.cell)) < ToeField.FoldGradient &&
        this.Top(point, up) is {} top) {
        return math.distance(from, top) <= reach && this.Contains(top) ? top : null;
      }
    }

    return null;
  }

  internal bool Contains(float2 point) => GroundCheck.Contains(this.outline, point);

  /// <summary>
  /// Whether the outline ends less than a distance uphill of a point.
  /// </summary>
  internal bool EndsUphill(float2 point, float distance) =>
    !this.Contains(point + (this.Up(point) * distance));

  private static int Count(float span, float cell) =>
    (int) math.ceil(span / cell) + 1 + (2 * ToeField.Margin);

  /// <summary>
  /// The highest point ahead of a point, or null when the field still rises two cells away.
  /// </summary>
  private float2? Top(float2 point, float2 up) {
    var step = up * (0.125f * this.cell);
    var steps = 16;

    while (this.Sample(point + step) > this.Sample(point)) {
      if (steps-- == 0) {
        return null;
      }

      point += step;
    }

    // The grid has the top on one of its nodes: it is where the slopes on its two sides meet.
    var side = up * (1.5f * this.cell);
    var past = 0.5f * (this.Sample(point + side) - this.Sample(point - side));

    return point + (up * math.clamp(past, -0.5f * this.cell, 0.5f * this.cell));
  }

  /// <summary>
  /// The distance to the toe at a point, by bilinear interpolation.
  /// </summary>
  private float Sample(float2 point) {
    var x = math.clamp((point.x - this.origin.x) / this.cell, 0f, this.countX - 1.001f);
    var z = math.clamp((point.y - this.origin.y) / this.cell, 0f, this.countZ - 1.001f);
    var node = ((int) z * this.countX) + (int) x;
    var u = x - (int) x;
    var v = z - (int) z;
    var below = math.lerp(this.distances[node], this.distances[node + 1], u);

    var above = math.lerp(
      this.distances[node + this.countX],
      this.distances[node + this.countX + 1],
      u
    );

    return math.lerp(below, above, v);
  }

  /// <summary>
  /// The way uphill at a point, of unit length.
  /// </summary>
  private float2 Up(float2 point) => math.normalizesafe(this.Slope(point, this.cell));

  /// <summary>
  /// The field's gradient at a point: unit length on a side of the breakwater, shorter where it
  /// folds.
  /// </summary>
  private float2 Slope(float2 point, float step) {
    var x = new float2(step, 0f);
    var z = new float2(0f, step);

    return new float2(
        this.Sample(point + x) - this.Sample(point - x),
        this.Sample(point + z) - this.Sample(point - z)
      ) /
      (2f * step);
  }
}

/// <summary>
/// Writes one node of a <see cref="ToeField"/>: its distance to the toe edges.
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

    for (var l = 0; l < this.polygon.Length; l++) {
      if (!this.toe[l]) {
        continue;
      }

      var a = this.polygon[l];
      var ab = this.polygon[(l + 1) % this.polygon.Length] - a;
      var u = math.saturate(math.dot(point - a, ab) / math.max(math.lengthsq(ab), 1e-6f));

      nearest = math.min(nearest, math.lengthsq(point - a - (u * ab)));
    }

    this.distances[index] = math.sqrt(nearest);
  }
}
