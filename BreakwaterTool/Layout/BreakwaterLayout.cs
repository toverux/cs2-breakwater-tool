using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace BreakwaterTool.Layout;

/// <summary>
/// Lays the stones of a breakwater over a polygon, on the terrain: the terrain is the mound, the
/// stones dress it, and the polygon only says where.
/// The toe is the edges it is given, or else the run of the polygon's edges in the bottom share of
/// its points' heights, or every edge when they are level.
/// Each row is a contour of the distance to the whole toe, clipped to the polygon: the first is the
/// toe, the next one row spacing further up the slope, until the polygon holds none (a revetment's
/// crest) or the rows of two sides meet (a jetty's spine). Its stones are placed fresh along it, a
/// stone spacing apart and each between two stones of the row below, a brick bond; a toe line's end
/// turns its rows round it, as on a breakwater's head.
/// </summary>
internal static class BreakwaterLayout {
  /// <summary>
  /// The most stones one breakwater may hold: over it, the layout lays nothing.
  /// A re-lay of this many protected stones costs about 0.6 s of frames, and 128 000 small props
  /// crashed the game in the prototype.
  /// </summary>
  internal const int Guard = 20_000;

  /// <summary>
  /// The farthest from the middle of the map, each way, that a polygon's points may be, in metres:
  /// past any map's edge. Where a float's step swallows a cell of the field or a stone spacing, the
  /// layout would throw or never end.
  /// </summary>
  private const float Farthest = 32_768f;

  #if DEBUG
  /// <summary>
  /// Whether dumped stones are fitted into a heap, or only tumbled: the comparison an agent or the
  /// user makes in the game.
  /// </summary>
  internal static bool DebugIsFitting { get; set; } = true;
  #else
  private const bool DebugIsFitting = true;
  #endif

  /// <param name="polygon">The breakwater's outline, in game metres, not closed.</param>
  /// <param name="shapes">The stones to lay; <see cref="LaidStone.Shape"/> indexes them.</param>
  /// <param name="toeEdges">
  /// Per edge, from point i to i + 1, whether it is toe; null, or a list of another length or
  /// without a toe edge, leaves the choice to <see cref="ToeEdges"/>.
  /// </param>
  internal static LayoutResult Lay(
    IReadOnlyList<float2> polygon,
    IReadOnlyList<StoneShape> shapes,
    BreakwaterParameters parameters,
    ILayoutHost host,
    IReadOnlyList<bool>? toeEdges = null
  ) {
    var area = BreakwaterLayout.Area(polygon);

    if (polygon.Count < 3 ||
      polygon.Any(static point => !math.all(math.abs(point) <= BreakwaterLayout.Farthest)) ||
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

    var isHeaped = BreakwaterLayout.IsHeaped(parameters);
    var laidShapes = isHeaped ? shapes.Select(static shape => shape.Heap.Rested).ToList() : shapes;
    var (kept, sizes, centres) = BreakwaterLayout.Kept(shapes, parameters);
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

    var toe = toeEdges is not null && toeEdges.Count == polygon.Count && toeEdges.Contains(true)
      ? toeEdges.ToArray()
      : BreakwaterLayout.ToeEdges(polygon, host, parameters);

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
      cell: BreakwaterLayout.Cell(math.min(stoneSpacing, rowSpacing)),
      host
    );

    var builder = new Builder(laidShapes, parameters, host, field) {
      Kept = kept,
      Centres = centres,
      MeanHeight = kept.Average(i => laidShapes[i].Height),
      StoneSpacing = stoneSpacing,
      RowSpacing = rowSpacing,
      Sampler = new RowSampler(stoneSpacing, rowSpacing)
    };

    var toeLines = BreakwaterLayout.ToeLines(polygon, toe);

    for (var layer = 0; layer < parameters.Layers; layer++) {
      builder.LayRows(toeLines, layer);
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

    var stones = isHeaped
      ? HeapFit.Fit(
        [
          .. builder.Stones.Select(stone => stone with {
              Rotation = math.mul(stone.Rotation, shapes[stone.Shape].Heap.Rest)
            }
          )
        ],
        [.. shapes.Select(static shape => shape.Heap)],
        host,
        parameters.Embed,
        parameters.Seed
      )
      : builder.Stones;

    return new LayoutResult {
      Verdict = LayoutVerdict.Laid,
      Stones = stones,
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
  /// Whether the parameters dump the stones, which are then fitted into a heap at rest and spaced
  /// by the room they take there.
  /// </summary>
  internal static bool IsHeaped(BreakwaterParameters parameters) =>
    parameters.Tumble > 0f && BreakwaterLayout.DebugIsFitting;

  /// <summary>
  /// The shapes the size tolerance keeps, as indices: those whose footprint area is within it of
  /// the median one.
  /// </summary>
  /// <returns>
  /// The indices kept, with each shape's footprint along and across the row once laid, and its
  /// footprint centre.
  /// </returns>
  internal static (int[] Kept, float2[] Sizes, float2[] Centres) Kept(
    IReadOnlyList<StoneShape> shapes,
    BreakwaterParameters parameters
  ) {
    var isHeaped = BreakwaterLayout.IsHeaped(parameters);
    var sizes = new float2[shapes.Count];
    var centres = new float2[shapes.Count];

    for (var i = 0; i < shapes.Count; i++) {
      // A heaped stone's footprint at rest, shrunk to the share of its rectangle it covers.
      sizes[i] = isHeaped
        ? shapes[i].Heap.Rested.Footprint(parameters.Embed, out centres[i]) *
          math.sqrt(shapes[i].Heap.Fill)
        : shapes[i].Footprint(parameters.Embed, out centres[i]);

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

    return (kept, sizes, centres);
  }

  /// <summary>
  /// Per edge, from point i to i + 1, whether it is in the bottom share of the boundary's heights,
  /// both its ends; every edge when the boundary is level.
  /// Where no edge is, the low part is a corner, on a slope falling across the outline: the toe is
  /// then the two edges that meet there.
  /// </summary>
  internal static bool[] ToeEdges(
    IReadOnlyList<float2> polygon,
    ILayoutHost host,
    BreakwaterParameters parameters
  ) {
    var heights = polygon.Select(host.HeightAt).ToArray();
    var low = heights.Min();
    var high = heights.Max();
    var toe = new bool[polygon.Count];

    for (var i = 0; i < polygon.Count; i++) {
      toe[i] = high - low < parameters.LevelHeight ||
        math.max(heights[i], heights[(i + 1) % polygon.Count]) <=
        low + (parameters.ToeShare * (high - low));
    }

    if (!toe.Contains(true)) {
      var lowest = Array.IndexOf(heights, low);

      toe[lowest] = true;
      toe[(lowest + polygon.Count - 1) % polygon.Count] = true;
    }

    return toe;
  }

  /// <summary>
  /// The field's step for the smaller of the spacings, in metres: a quarter of it draws a seam
  /// well.
  /// </summary>
  // [PERF] Half a metre or half of it for small stones, which take the most cells, in place of a
  // quarter for all.
  private static float Cell(float spacing) => math.min(math.max(0.5f, spacing / 4f), spacing / 2f);

  /// <summary>
  /// The toe as lines: each run of consecutive toe edges, open, or the whole polygon, closed, when
  /// every edge is toe.
  /// </summary>
  private static List<RowLine> ToeLines(IReadOnlyList<float2> polygon, bool[] toe) {
    if (toe.All(static edge => edge)) {
      return [new RowLine([.. polygon], true)];
    }

    var runs = new List<List<float2>>();

    // Start after an edge off the toe, so that no run wraps round the start.
    var start = Array.IndexOf(toe, false) + 1;
    List<float2>? run = null;

    for (var k = 0; k < polygon.Count; k++) {
      var i = (start + k) % polygon.Count;

      if (!toe[i]) {
        run = null;

        continue;
      }

      if (run is null) {
        run = [polygon[i]];
        runs.Add(run);
      }

      run.Add(polygon[(i + 1) % polygon.Count]);
    }

    return runs.ConvertAll(static points => new RowLine([.. points], false));
  }

  /// <summary>
  /// The rows of one layout and the stones they hold so far.
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

    /// <summary>
    /// How near two stones of one layer may be placed, as a share of the smaller of the stone and
    /// row spacings: where rows from two sides meet, it keeps the stones of one from lying on the
    /// other's.
    /// </summary>
    private const float LeastGap = 2f / 3f;

    /// <summary>
    /// How near two stones of one layer may lie, as a share of the smaller of the stone and row
    /// spacings, once each is off its place by the irregularity.
    /// </summary>
    private const float LeastApart = 0.5f;

    // Every random draw of the layout, in a fixed order, hence a single mutable stream.
    private Random random = new(math.max(1u, (parameters.Seed * 7919u) + 1u));

    internal required int[] Kept { get; init; }

    internal required float2[] Centres { get; init; }

    internal required float MeanHeight { get; init; }

    internal required float StoneSpacing { get; init; }

    internal required float RowSpacing { get; init; }

    internal required RowSampler Sampler { get; init; }

    internal List<LaidStone> Stones { get; } = [];

    internal int Rows { get; private set; }

    /// <summary>
    /// How far the barest spot of an even bond is from its stones: the radius of the circle through
    /// three of them.
    /// </summary>
    private float Room =>
      ((0.25f * this.StoneSpacing * this.StoneSpacing) + (this.RowSpacing * this.RowSpacing)) /
      math.max(2f * this.RowSpacing, this.StoneSpacing);

    /// <summary>
    /// Lays a layer's rows, the first on the toe, until none is left.
    /// </summary>
    internal void LayRows(IReadOnlyList<RowLine> toe, int layer) {
      // A second layer sits half a row and half a stone off the first.
      var shift = layer * 0.5f;
      var level = shift * this.RowSpacing;
      var gap = this.Gap(this.RowSpacing);
      var taken = new PlaceGrid(gap, this.Room);
      var below = new List<RowPlace>();

      // Past the guard the layout is refused anyway: an outline that crosses itself misleads the
      // estimate, which nets its lobes' areas.
      while (level <= field.Highest && this.Stones.Count <= BreakwaterLayout.Guard) {
        // The toe's own row is laid however short its lines are.
        var lines = level > 0f
          ? field.Contours(level).Where(line => !this.IsSliver(line))
          : toe;

        var row = new List<RowPlace>();
        var corners = new List<RowPlace>();

        foreach (var place in this.Sampler.Place(lines, below, shift, corners)) {
          if (taken.TryTake(place, gap)) {
            row.Add(place);
          }
        }

        // At an edge off the toe, the roomiest spot is on the edge itself, and a stone there would
        // stand half outside: the rows end there anyway, as at the crest.
        foreach (var corner in corners) {
          if (field.OffToeDistance(corner.Position) >=
            math.max(this.StoneSpacing, this.RowSpacing)) {
            taken.Watch(corner);
          }
        }

        this.Rows += row.Count > 0 ? 1 : 0;

        // Rows one spacing apart on the slope: closer seen from above where it is steeper.
        var rise = this.RowSpacing * math.max(0.5f, this.Emit(row, layer));

        this.Emit(this.Closing(row, level, rise, taken), layer);

        level += rise;
        below = row;
      }

      this.Emit(taken.TakeHoles(field), layer);
    }

    /// <summary>
    /// The least distance between two places, seen from above, where rows are a distance apart.
    /// </summary>
    private float Gap(float rows) => Builder.LeastGap * math.min(this.StoneSpacing, rows);

    /// <summary>
    /// Whether a line is a piece of row too short for two stones that the outline ends just above:
    /// the stone it would hold stands over the crest.
    /// </summary>
    private bool IsSliver(RowLine line) =>
      !line.IsClosed &&
      line.Length < this.StoneSpacing &&
      field.EndsUphill((line.Points[0] + line.Points[^1]) * 0.5f, 0.25f * this.RowSpacing);

    /// <summary>
    /// The places that close a row with the one on the other side of the spine, where the next rows
    /// would come too near each other: on the spine when it is within a rise, otherwise on either
    /// side of it, as far from each other as from their rows.
    /// </summary>
    private List<RowPlace> Closing(
      List<RowPlace> row,
      float level,
      float rise,
      PlaceGrid taken
    ) {
      var places = new List<RowPlace>();
      var least = this.ClosingGap(rise);

      // Past the gap, so that the next row finds room on the spine where this one leaves it open.
      var reach = 1.7f * rise;

      // Off the row, whose own line may be the toe, where the field folds too, and low enough for
      // its line to stay clear of the spine.
      var start = math.max(0.25f * rise, field.LowestWhole - level);

      // [PERF] No line is traced where no sides meet, in place of a climb from each place.
      if (!field.HasFold(level + start, level + reach)) {
        return places;
      }

      foreach (var place in this.Sampler.Place(field.Contours(level + start), row, 0f)) {
        if (field.Climb(place.Position, reach - start) is not {} spine) {
          continue;
        }

        var climb = math.distance(place.Position, spine);
        var height = start + climb;

        var closing = place with {
          Position = height <= rise
            ? spine
            : math.lerp(place.Position, spine, ((2f / 3f * height) - start) / climb)
        };

        // Where the outline bends in, it can pass between the row and the spine.
        if (field.Contains(closing.Position) && taken.TryTake(closing, least)) {
          places.Add(closing);
        }
      }

      return places;
    }

    /// <summary>
    /// The least distance between a closing place and the others: the rows' gap where they rise
    /// less, as they come closer near the spine; but as far as keeps two stones off their places no
    /// nearer than <see cref="LeastApart"/>, and at most the rows' own gap.
    /// </summary>
    // Off their places by the default irregularity at most: past it, the player asked for disorder,
    // and the spine would show the ground where a closing place is refused.
    private float ClosingGap(float rise) {
      var offPlace = math.min(parameters.Irregularity, BreakwaterParameters.Default.Irregularity) *
        this.StoneSpacing;

      var apart = (Builder.LeastApart * math.min(this.StoneSpacing, this.RowSpacing)) +
        (2f * math.SQRT2 * offPlace);

      return math.max(this.Gap(rise), math.min(apart, this.Gap(this.RowSpacing)));
    }

    /// <summary>
    /// Lays a stone on each place of a row, and returns the mean cosine of the row's slope, 1 for
    /// an empty row.
    /// </summary>
    private float Emit(List<RowPlace> row, int layer) {
      if (row.Count == 0) {
        return 1f;
      }

      var rowShape = this.Kept[this.random.NextInt(this.Kept.Length)];
      var cosines = 0f;

      foreach (var (place, direction) in row) {
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

        var along = new float3(direction.x, 0f, direction.y);
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

      return cosines / row.Count;
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
  }
}
