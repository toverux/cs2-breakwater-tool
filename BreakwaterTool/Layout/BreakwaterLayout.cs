using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace BreakwaterTool.Layout;

/// <summary>
/// Lays the stones of a breakwater over a polygon, on the terrain: the terrain is the mound, the
/// stones dress it, and the polygon only says where.
/// The toe is the run of the polygon's edges in the bottom 40 % of its points' heights, or every
/// edge when they are within 2 m of level. The first row follows the toe, each next row one row
/// spacing further up the slope, until it leaves the polygon (a revetment's crest) or meets the
/// rows climbing the other side (a jetty's spine). Each row's stones sit at the midpoints of the
/// row below, a brick bond; a row from a run of toe edges also grows round the run's ends, as on a
/// breakwater's head.
/// </summary>
internal static class BreakwaterLayout {
  /// <summary>
  /// The most stones one breakwater may hold: over it, the layout lays nothing.
  /// A re-lay of this many protected stones costs about 0.6 s of frames, and 128 000 small props
  /// crashed the game in the prototype.
  /// </summary>
  internal const int Guard = 20_000;

  /// <summary>
  /// A boundary whose points are within this height of each other is level: all of it is toe.
  /// </summary>
  private const float LevelHeight = 2f;

  /// <summary>
  /// The share of the boundary's height range, from its lowest point, that holds the toe.
  /// </summary>
  private const float ToeShare = 0.4f;

  /// <param name="polygon">The breakwater's outline, in game metres, not closed.</param>
  /// <param name="shapes">The stones to lay; <see cref="LaidStone.Shape"/> indexes them.</param>
  internal static LayoutResult Lay(
    IReadOnlyList<float2> polygon,
    IReadOnlyList<StoneShape> shapes,
    BreakwaterParameters parameters,
    ILayoutHost host
  ) {
    var area = BreakwaterLayout.Area(polygon);

    if (polygon.Count < 3 ||
      polygon.Any(static point => !math.all(math.isfinite(point))) ||
      area <= 0f) {
      return new LayoutResult {
        Verdict = LayoutVerdict.NoArea
      };
    }

    if (shapes.Count == 0) {
      return new LayoutResult {
        Verdict = LayoutVerdict.NoStone
      };
    }

    // Footprints along and across the row once laid, and the shapes within the size tolerance.
    var sizes = new float2[shapes.Count];
    var centres = new float2[shapes.Count];

    for (var i = 0; i < shapes.Count; i++) {
      sizes[i] = shapes[i].Footprint(parameters.Embed, out centres[i]);

      if (parameters.IsLongAxisAcross) {
        sizes[i] = sizes[i].yx;
      }
    }

    // Stored as floats, so the median's own ratio is exactly 1 in the game's double arithmetic too.
    var areas = sizes.Select(static size => size.x * size.y).ToArray();
    var medianArea = areas.OrderBy(static a => a).ElementAt(areas.Length / 2);

    var kept = Enumerable.Range(0, shapes.Count)
      .Where(i => Math.Abs((areas[i] / medianArea) - 1f) <= parameters.SizeTolerance)
      .ToArray();

    var leftOut = Enumerable.Range(0, shapes.Count).Except(kept).ToArray();
    var meanSize = kept.Aggregate(float2.zero, (sum, i) => sum + sizes[i]) / kept.Length;
    var stoneSpacing = meanSize.x * parameters.Packing;
    var rowSpacing = meanSize.y * parameters.RowPacking;
    var spacings = new float2(stoneSpacing, rowSpacing);

    // Stones that show no footprint leave nothing to space the rows by.
    if (!math.all(math.isfinite(spacings) & (spacings > 0f))) {
      return new LayoutResult {
        Verdict = LayoutVerdict.NoStone,
        LeftOut = leftOut
      };
    }

    var toe = BreakwaterLayout.ToeEdges(polygon, host);

    var estimate = (double) area / (stoneSpacing * rowSpacing) * parameters.Layers;

    if (estimate > BreakwaterLayout.Guard) {
      return new LayoutResult {
        Verdict = LayoutVerdict.OverGuard,
        Estimate = (int) Math.Min(estimate, int.MaxValue),
        ToeEdges = toe,
        LeftOut = leftOut,
        StoneSpacing = stoneSpacing,
        RowSpacing = rowSpacing
      };
    }

    var field = new ToeField(
      polygon,
      toe,
      cell: math.min(0.5f, math.min(stoneSpacing, rowSpacing) / 4f),
      margin: 2f * math.max(stoneSpacing, rowSpacing),
      host
    );

    var builder = new Builder(shapes, parameters, host, field) {
      Kept = kept,
      Centres = centres,
      MeanHeight = kept.Average(i => shapes[i].Height),
      StoneSpacing = stoneSpacing,
      RowSpacing = rowSpacing
    };

    var lines = BreakwaterLayout.ToeLines(polygon, toe);

    for (var layer = 0; layer < parameters.Layers; layer++) {
      foreach (var line in lines) {
        builder.LayRows(line, layer);
      }
    }

    // Rows close up on steep ground, so a slope can hold more stones than its area's estimate.
    if (builder.Stones.Count > BreakwaterLayout.Guard) {
      return new LayoutResult {
        Verdict = LayoutVerdict.OverGuard,
        Estimate = (int) estimate,
        ToeEdges = toe,
        LeftOut = leftOut,
        Rows = builder.Rows,
        FieldCells = field.CellCount,
        StoneSpacing = stoneSpacing,
        RowSpacing = rowSpacing
      };
    }

    return new LayoutResult {
      Verdict = builder.Stones.Count > 0 ? LayoutVerdict.Laid : LayoutVerdict.NoRow,
      Stones = builder.Stones,
      Estimate = (int) estimate,
      ToeEdges = toe,
      LeftOut = leftOut,
      Rows = builder.Rows,
      FieldCells = field.CellCount,
      StoneSpacing = stoneSpacing,
      RowSpacing = rowSpacing
    };
  }

  /// <summary>
  /// The polygon's area, whichever way it turns, by the shoelace formula.
  /// </summary>
  internal static float Area(IReadOnlyList<float2> polygon) {
    var twice = 0f;

    for (var i = 0; i < polygon.Count; i++) {
      var a = polygon[i];
      var b = polygon[(i + 1) % polygon.Count];

      twice += (a.x * b.y) - (a.y * b.x);
    }

    return Math.Abs(twice) / 2f;
  }

  /// <summary>
  /// Per edge, from point i to i + 1, whether it is in the bottom share of the boundary's heights,
  /// both its ends; every edge when the boundary is level.
  /// Where no edge is, the low part is a corner, on a slope falling across the outline: the toe is
  /// then the two edges that meet there.
  /// </summary>
  internal static bool[] ToeEdges(IReadOnlyList<float2> polygon, ILayoutHost host) {
    var heights = polygon.Select(host.HeightAt).ToArray();
    var low = heights.Min();
    var high = heights.Max();
    var toe = new bool[polygon.Count];

    for (var i = 0; i < polygon.Count; i++) {
      toe[i] = high - low < BreakwaterLayout.LevelHeight ||
        math.max(heights[i], heights[(i + 1) % polygon.Count]) <=
        low + (BreakwaterLayout.ToeShare * (high - low));
    }

    if (!toe.Contains(true)) {
      var lowest = Array.IndexOf(heights, low);

      toe[lowest] = true;
      toe[(lowest + polygon.Count - 1) % polygon.Count] = true;
    }

    return toe;
  }

  /// <summary>
  /// The toe as polylines: each run of consecutive toe edges, open, or the whole polygon closed on
  /// its first point when every edge is toe.
  /// </summary>
  private static List<List<float2>> ToeLines(IReadOnlyList<float2> polygon, bool[] toe) {
    var lines = new List<List<float2>>();

    if (toe.All(static edge => edge)) {
      lines.Add([.. polygon, polygon[0]]);

      return lines;
    }

    // Start after an edge off the toe, so that no run wraps round the start.
    var start = Array.IndexOf(toe, false) + 1;
    List<float2>? line = null;

    for (var k = 0; k < polygon.Count; k++) {
      var i = (start + k) % polygon.Count;

      if (!toe[i]) {
        line = null;

        continue;
      }

      if (line is null) {
        line = [polygon[i]];
        lines.Add(line);
      }

      line.Add(polygon[(i + 1) % polygon.Count]);
    }

    return lines;
  }

  /// <summary>
  /// Points along a polyline, as many as its length holds steps, rounded, spread evenly so that a
  /// closed loop has no short gap where it ends; the first at phase times the spread.
  /// A line shorter than a step gets one point.
  /// </summary>
  private static IEnumerable<float2> Resample(List<float2> line, float step, float phase) {
    var length = 0f;

    for (var k = 1; k < line.Count; k++) {
      length += math.distance(line[k - 1], line[k]);
    }

    var count = Math.Max(1, (int) math.round(length / step));
    var spread = length / count;
    var next = phase * spread;
    var walked = 0f;

    for (var k = 1; k < line.Count && count > 0; k++) {
      var segment = math.distance(line[k - 1], line[k]);

      while (next <= walked + segment && count > 0) {
        yield return math.lerp(line[k - 1], line[k], segment > 0f ? (next - walked) / segment : 0f);

        next += spread;
        count--;
      }

      walked += segment;
    }
  }

  /// <summary>
  /// The rows of one layout, laid one toe line at a time, and the stones they hold so far.
  /// </summary>
  private sealed class Builder(
    IReadOnlyList<StoneShape> shapes,
    BreakwaterParameters parameters,
    ILayoutHost host,
    ToeField field
  ) {
    /// <summary>
    /// The distance on the ground, round a stone, over which its tilt is read.
    /// </summary>
    private const float TiltReach = 1.5f;

    // Every random draw of the layout, in a fixed order, hence a single mutable stream.
    private Random random = new(math.max(1u, (parameters.Seed * 7919u) + 1u));

    internal required int[] Kept { get; init; }

    internal required float2[] Centres { get; init; }

    internal required float MeanHeight { get; init; }

    internal required float StoneSpacing { get; init; }

    internal required float RowSpacing { get; init; }

    internal List<LaidStone> Stones { get; } = [];

    internal int Rows { get; private set; }

    /// <summary>
    /// Lays the rows that climb from a toe line, the first on it, until none is left.
    /// </summary>
    internal void LayRows(List<float2> line, int layer) {
      // The polygon itself closes on its first point; a run of toe edges is open.
      var closed = line.Count > 2 && line[0].Equals(line[^1]);

      // A second layer sits half a row and half a stone off the first.
      var shift = layer * 0.5f;

      var row = BreakwaterLayout.Resample(line, this.StoneSpacing, shift)
        .Select(point => field.Move(point, shift * this.RowSpacing, this.RowSpacing))
        .ToList();

      // Past the guard the layout is refused anyway: an outline that crosses itself misleads the
      // estimate, which nets its lobes' areas.
      for (var t = shift * this.RowSpacing;
        row.Count > 0 && this.Stones.Count <= BreakwaterLayout.Guard;) {
        var slope = this.Emit(row, layer);

        // Rows one spacing apart on the slope: closer seen from above where it is steeper.
        t += this.RowSpacing * math.max(0.5f, slope);
        row = this.NextRow(row, closed, t);

        if (!closed) {
          this.Extend(row, t);
        }
      }
    }

    /// <summary>
    /// Lays a stone on each place of a row, and returns the mean cosine of the row's slope.
    /// </summary>
    private float Emit(List<float2?> row, int layer) {
      this.Rows++;

      var rowShape = this.Kept[this.random.NextInt(this.Kept.Length)];
      var cosines = 0f;
      var laid = 0;

      foreach (var anchor in row) {
        if (anchor is not {} place) {
          continue;
        }

        var shape = this.random.NextFloat() < parameters.Mixing
          ? this.Kept[this.random.NextInt(this.Kept.Length)]
          : rowShape;

        var xz = place +
          (this.random.NextFloat2(-1f, 1f) *
            parameters.Irregularity *
            this.StoneSpacing);

        var floor = host.HeightAt(xz);
        var normal = this.Normal(xz);

        cosines += normal.y;
        laid++;

        var gradient = field.Gradient(place);
        var along = new float3(-gradient.y, 0f, gradient.x);
        var up = math.normalize(math.lerp(new float3(0f, 1f, 0f), normal, parameters.FollowSlope));
        var x = math.normalizesafe(along - (math.dot(along, up) * up), new float3(1f, 0f, 0f));
        var frame = quaternion.LookRotationSafe(math.cross(x, up), up);

        // Half the stones turned round, for variety without breaking the row.
        var turn = this.random.NextFloat(-parameters.TurnJitter, parameters.TurnJitter);

        var yaw = math.radians(turn) +
          (this.random.NextBool() ? math.PI : 0f) +
          (parameters.IsLongAxisAcross ? math.PI / 2f : 0f);

        var laidFrame = math.mul(frame, quaternion.RotateY(yaw));

        // The footprint's centre on the place, not the prefab's pivot.
        var centre = this.Centres[shape];
        var offset = math.rotate(laidFrame, new float3(centre.x, 0f, centre.y));
        var stone = shapes[shape];
        var byBottom = floor - (parameters.Embed * stone.Height) - stone.MinY;
        var byTop = floor + ((1f - parameters.Embed) * this.MeanHeight) - stone.MaxY;

        var position = new float3(
          xz.x - offset.x,
          math.lerp(byBottom, byTop, parameters.LevelTops) -
          (layer * parameters.UnderLayerDepth * this.MeanHeight),
          xz.y - offset.z
        );

        var rotation = math.mul(laidFrame, quaternion.RotateY(stone.Angle));

        if (parameters.Tumble > 0f) {
          // About a random axis through the place on the ground, drawn only when tumbling, so that
          // a laid breakwater's stones do not depend on this parameter.
          var tumble = quaternion.AxisAngle(
            this.random.NextFloat3Direction(),
            this.random.NextFloat(-1f, 1f) * parameters.Tumble * math.PI
          );

          var pivot = new float3(xz.x, floor, xz.y);

          position = pivot + math.rotate(tumble, position - pivot);
          rotation = math.mul(tumble, rotation);
        }

        this.Stones.Add(new LaidStone(shape, position, rotation));
      }

      return laid > 0 ? cosines / laid : 1f;
    }

    /// <summary>
    /// The terrain's unit normal at a point, read over <see cref="TiltReach"/> each way.
    /// </summary>
    private float3 Normal(float2 xz) {
      var dx = new float2(Builder.TiltReach, 0f);
      var dz = new float2(0f, Builder.TiltReach);

      return math.normalize(
        new float3(
          (host.HeightAt(xz - dx) - host.HeightAt(xz + dx)) / (2f * Builder.TiltReach),
          1f,
          (host.HeightAt(xz - dz) - host.HeightAt(xz + dz)) / (2f * Builder.TiltReach)
        )
      );
    }

    /// <summary>
    /// The row at distance t from the toe: each place of the previous row moved onto the new
    /// contour, then stones at the midpoints between neighbours, one per stone spacing of the gap.
    /// A place that cannot reach the contour is a null, a break in the row.
    /// </summary>
    private List<float2?> NextRow(List<float2?> previous, bool closed, float t) {
      var moved = previous
        .Select(point => point is {} place ? field.Move(place, t, this.RowSpacing) : null)
        .ToList();

      var row = new List<float2?>();
      var pairs = closed ? moved.Count : moved.Count - 1;

      for (var i = 0; i < pairs; i++) {
        if (moved[i] is not {} a || moved[(i + 1) % moved.Count] is not {} b) {
          row.Add(null);

          continue;
        }

        var gap = math.distance(a, b);

        // A wider gap is a place that jumped, across a narrow inlet: a break. A convex bend of the
        // toe widens a gap by about a row spacing times its angle per row, well under it.
        if (gap > 4f * this.StoneSpacing) {
          row.Add(null);

          continue;
        }

        var count = (int) math.round(gap / this.StoneSpacing);

        for (var k = 0; k < count; k++) {
          row.Add(math.lerp(a, b, (k + 0.5f) / count));
        }
      }

      return row.Any(static point => point is not null) ? row : [];
    }

    /// <summary>
    /// Prolongs an open row from each end along its contour, a stone at a time, while it stays
    /// inside the polygon: round the end of a toe run the contours are arcs, and the rows wrap
    /// round it as on a breakwater's head.
    /// </summary>
    private void Extend(List<float2?> row, float t) {
      if (row.Count < 2) {
        return;
      }

      this.ExtendTail(row, t);
      row.Reverse();
      this.ExtendTail(row, t);
      row.Reverse();
    }

    private void ExtendTail(List<float2?> row, float t) {
      if (row[^1] is not {} last || row[^2] is not {} beforeLast) {
        return;
      }

      var point = last;
      var direction = math.normalizesafe(last - beforeLast);
      var existing = row.Count;

      // Bounded, as a contour that loops would otherwise never end.
      for (var k = 0; k < 1000; k++) {
        if (field.Move(point + (direction * this.StoneSpacing), t, this.RowSpacing, 0.15f) is not {
          } next ||
          field.Sample(next) < 0f) {
          return;
        }

        var step = next - point;

        // Stop on a fold back, or on reaching stones already laid in this row.
        if (math.length(step) < 0.5f * this.StoneSpacing ||
          math.dot(math.normalize(step), direction) < 0.5f) {
          return;
        }

        for (var i = 0; i < existing - 1; i++) {
          if (row[i] is {} other && math.distance(other, next) < this.StoneSpacing) {
            return;
          }
        }

        direction = math.normalize(step);
        point = next;
        row.Add(point);
      }
    }
  }
}
