using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace BreakwaterTool.Layout;

/// <summary>
/// Places stones along a row's lines, a stone spacing apart, in a brick bond with the row below.
/// </summary>
/// <param name="spacing">The distance between stones along a row.</param>
/// <param name="rowSpacing">The distance between rows.</param>
internal sealed class RowSampler(float spacing, float rowSpacing) {
  /// <summary>
  /// How far back along its row a stone may shift, in stone spacings, to sit between two stones of
  /// the row below: enough to hold the bond where rows close up, too little to crowd them.
  /// </summary>
  private const float Pull = 0.22f;

  /// <summary>
  /// How far ahead it may, where rows fan out: less, since a wide joint shows the ground.
  /// </summary>
  private const float Stretch = 0.125f;

  /// <summary>
  /// How far ahead, in stone spacings, its place in the bond may be before a stone takes the one
  /// behind it instead: the row gains a stone, in place of wide joints.
  /// </summary>
  private const float Drift = 0.4f;

  /// <summary>
  /// How far each way along a line from a stone its direction is read, over as much again, in stone
  /// spacings: past the diagonal that cuts a corner of the line, a cell long.
  /// </summary>
  private const float Bend = 0.2f;

  /// <summary>
  /// The cosine of the turn between the directions read each way, under which a line has a corner.
  /// </summary>
  private const float Corner = 0.95f;

  /// <summary>
  /// How far past an end of its line a stone stands on the end, in stone spacings.
  /// </summary>
  private const float Past = 0.05f;

  // How far from a stone the two stones of the row below that it sits between may stand.
  private readonly float reach = math.length(new float2(rowSpacing, spacing));

  private readonly List<float2> near = [];

  /// <summary>
  /// The places of a row, line after line.
  /// </summary>
  /// <param name="lines">The lines the row follows.</param>
  /// <param name="below">The places of the row below, empty for the first row.</param>
  /// <param name="phase">
  /// Where the first stone of an open line stands when no row below faces it, in stone spacings.
  /// </param>
  /// <param name="corners">
  /// Where the lines turn, if asked: a place at each corner, along the side it turns from.
  /// </param>
  internal List<RowPlace> Place(
    IEnumerable<RowLine> lines,
    IReadOnlyList<RowPlace> below,
    float phase,
    List<RowPlace>? corners = null
  ) {
    // [PERF] Asked per stone for the two it sits between, in place of every place of the row below
    // projected on the line.
    var index = new SpatialIndex(this.reach);
    var places = new List<RowPlace>();

    foreach (var place in below) {
      index.Add(place.Position);
    }

    foreach (var line in lines) {
      var walk = new Walk(line) {
        Bend = RowSampler.Bend * spacing,
        IsFirstRow = below.Count == 0
      };

      if (walk.Length < spacing) {
        places.Add(RowSampler.Lone(walk, below));
      }
      else {
        this.Along(walk, index, phase, places);
      }

      corners?.AddRange(walk.Corners(spacing));
    }

    return places;
  }

  /// <summary>
  /// The place in the middle of a line too short for two. A ring that short is about a cell of the
  /// field across, and runs as the grid does: its place takes the direction of the nearest below.
  /// </summary>
  private static RowPlace Lone(Walk walk, IReadOnlyList<RowPlace> below) {
    if (!walk.IsClosed) {
      return walk.At(walk.Length / 2f);
    }

    var centre = walk.Centre();

    return below.Count == 0
      ? centre
      : centre with {
        Along = below.OrderBy(place => math.distancesq(place.Position, centre.Position))
          .First()
          .Along
      };
  }

  private void Along(Walk walk, SpatialIndex below, float phase, List<RowPlace> places) {
    var step = walk.IsClosed
      ? walk.Length / math.round(walk.Length / spacing)
      : spacing;

    var at = this.Start(walk, below, step) ?? (walk.IsClosed ? 0f : phase * step);
    var end = walk.IsClosed ? at + walk.Length - (0.5f * step) : walk.Length;

    while (at <= end + (RowSampler.Past * step)) {
      places.Add(walk.At(at));

      at += step;

      var shift = (this.Shift(walk, below, at) ?? 0f) / step;

      at += step *
        (shift > RowSampler.Drift
          ? -RowSampler.Pull
          : math.clamp(shift, -RowSampler.Pull, RowSampler.Stretch));
    }
  }

  /// <summary>
  /// Where a line's first stone stands, the bond continued back to the line's start, or null where
  /// no row below faces the line.
  /// </summary>
  private float? Start(Walk walk, SpatialIndex below, float step) {
    for (var at = 0f; at <= walk.Length; at += step) {
      if (this.Shift(walk, below, at) is {} shift) {
        return at + shift - (math.floor(((at + shift) / step) + RowSampler.Past) * step);
      }
    }

    return null;
  }

  /// <summary>
  /// How far along a line from a length a stone is as far from one as from the other of the two
  /// stones of the row below it stands between. Null past an end of that row, and where the two are
  /// not about a stone spacing apart.
  /// </summary>
  private float? Shift(Walk walk, SpatialIndex below, float at) {
    var (point, along) = walk.On(at);
    var behind = (Length: float.MinValue, Offset: float2.zero);
    var ahead = (Length: float.MaxValue, Offset: float2.zero);

    below.Near(point, this.near);

    foreach (var place in this.near) {
      var offset = place - point;
      var length = math.dot(offset, along);

      // The row below is downhill, on the line's right.
      if ((along.x * offset.y) - (along.y * offset.x) > 0f) {
        continue;
      }

      if (length > 0f && length < ahead.Length) {
        ahead = (length, offset);
      }
      else if (length <= 0f && length > behind.Length) {
        behind = (length, offset);
      }
    }

    var gap = ahead.Length - behind.Length;

    return gap > 0.5f * spacing && gap < 1.5f * spacing
      ? (math.lengthsq(ahead.Offset) - math.lengthsq(behind.Offset)) / (2f * gap)
      : null;
  }

  /// <summary>
  /// A line walked by its length.
  /// </summary>
  private sealed class Walk {
    // [PERF] Arrays, in place of lists.
    private readonly float2[] points;

    // The length walked at each point.
    private readonly float[] lengths;

    // The last segment found, where the next search starts.
    private int segment;

    internal Walk(RowLine line) {
      // A closed line comes back to its first point.
      this.points = line.IsClosed ? [.. line.Points, line.Points[0]] : line.Points;
      this.IsClosed = line.IsClosed;
      this.lengths = new float[this.points.Length];

      for (var i = 1; i < this.points.Length; i++) {
        this.lengths[i] = this.lengths[i - 1] + math.distance(this.points[i - 1], this.points[i]);
      }
    }

    /// <summary>
    /// How far each way the line's direction is read, and over how much.
    /// </summary>
    internal required float Bend { get; init; }

    /// <summary>
    /// Whether the line is of the first row, where a stone on a corner stands across the turn; past
    /// it, such a stone follows the side of the corner it is on.
    /// </summary>
    internal required bool IsFirstRow { get; init; }

    internal bool IsClosed { get; }

    internal float Length => this.lengths[^1];

    internal RowPlace At(float length) {
      var (point, side) = this.On(length);
      var ahead = this.Direction(length + this.Bend);
      var behind = this.Direction(length - (2f * this.Bend));
      var isCorner = math.dot(ahead, behind) < RowSampler.Corner;

      var along = isCorner && !this.IsFirstRow
        ? math.dot(ahead, side) > math.dot(behind, side) ? ahead : behind
        : ahead + behind;

      // Past its ends, a short line has no direction to read either way.
      return new RowPlace(point, math.normalizesafe(along, side));
    }

    /// <summary>
    /// A place at each corner of the line, the sharpest of the points that turn as a stone on a
    /// corner does, along the side it turns from, read a stone back: the line cuts a corner across
    /// a cell or two, which a read nearer would take.
    /// </summary>
    internal IEnumerable<RowPlace> Corners(float spacing) {
      // A closed line comes back to its first point, its last one.
      var last = this.points.Length - 2;
      var sharpest = (Turn: RowSampler.Corner, At: -1, Along: float2.zero);

      for (var k = this.IsClosed ? 0 : 1; k <= last; k++) {
        // Near an end of an open line, there is no direction to read on one side.
        if (!this.IsClosed &&
          (this.lengths[k] < 2f * this.Bend || this.lengths[k] > this.Length - (2f * this.Bend))) {
          continue;
        }

        var behind = this.Direction(this.lengths[k] - (2f * this.Bend));
        var turn = math.dot(this.Direction(this.lengths[k] + this.Bend), behind);

        if (turn < sharpest.Turn) {
          sharpest = (turn, k, this.Direction(this.lengths[k] - spacing));

          continue;
        }

        // A corner ends where its points stop turning.
        if (sharpest.At >= 0 && turn >= RowSampler.Corner) {
          yield return new RowPlace(this.points[sharpest.At], sharpest.Along);

          sharpest = (RowSampler.Corner, -1, float2.zero);
        }
      }

      if (sharpest.At >= 0) {
        yield return new RowPlace(this.points[sharpest.At], sharpest.Along);
      }
    }

    /// <summary>
    /// The middle of a closed line too short to hold a ring of stones, along the line's first side.
    /// </summary>
    internal RowPlace Centre() {
      var sum = float2.zero;

      for (var i = 0; i < this.points.Length - 1; i++) {
        sum += this.points[i];
      }

      return new RowPlace(sum / (this.points.Length - 1), this.On(0f).Side);
    }

    /// <summary>
    /// The point at a length, and the direction there of the line's side that holds it.
    /// </summary>
    internal (float2 Point, float2 Side) On(float length) {
      length = this.Within(length);

      var k = this.Segment(length);
      var side = (this.points[k + 1] - this.points[k]) / this.Span(k);

      return (this.points[k] + (side * (length - this.lengths[k])), side);
    }

    /// <summary>
    /// The line's direction from a length on, none past an end of the line.
    /// </summary>
    private float2 Direction(float from) =>
      math.normalizesafe(this.Point(from + this.Bend) - this.Point(from));

    private float2 Point(float length) {
      length = this.Within(length);

      var k = this.Segment(length);

      return math.lerp(
        this.points[k],
        this.points[k + 1],
        (length - this.lengths[k]) / this.Span(k)
      );
    }

    /// <summary>
    /// A length brought back on the line: round a closed one, to the end of an open one.
    /// </summary>
    private float Within(float length) =>
      this.IsClosed
        ? length - (math.floor(length / this.Length) * this.Length)
        : math.clamp(length, 0f, this.Length);

    private float Span(int at) => math.max(this.lengths[at + 1] - this.lengths[at], 1e-6f);

    /// <summary>
    /// The segment that holds a length.
    /// </summary>
    private int Segment(float length) {
      // [PERF] From the last one found, in place of a search of the line: the lengths asked for
      // mostly grow or stay near.
      while (this.segment > 0 && length < this.lengths[this.segment]) {
        this.segment--;
      }

      while (this.segment < this.points.Length - 2 && length >= this.lengths[this.segment + 1]) {
        this.segment++;
      }

      return this.segment;
    }
  }
}
