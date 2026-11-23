using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace BreakwaterTool.Layout;

internal sealed partial class ToeField {
  // [PERF] Tables, in place of a search of the next cell's lines for the one that goes on.
  // Per mask of a cell's corners, then per side of the cell: the side by which the line that enters
  // there leaves, and the side by which the one that leaves there entered; -1 where none does.
  private static readonly int[] Leaves = [
    .. Enumerable.Range(0, 64).Select(static at => ToeField.LeaveOf(at / 4, at % 4))
  ];

  private static readonly int[] Enters = [
    .. Enumerable.Range(0, 64)
      .Select(static at =>
        Array.IndexOf(ToeField.Leaves, at % 4, at / 4 * 4, 4) is var found and >= 0
          ? found % 4
          : -1
      )
  ];

  // The arrays below are set once, by SortCells from the constructor, which readonly forbids.
  private CellState[] states = [];

  // [PERF] The cells inside, by the least distance of their corners, in buckets of a cell's width,
  // in place of a scan of the grid at each level, which only crosses the few buckets below it.
  private int[] cells = [];

  private int[] buckets = [];

  // Per distance, in steps of a cell's width, whether the field folds there.
  private bool[] folds = [];

  // [PERF] Per cell and line through it, the level that last took it, in place of a dictionary of
  // the segments' ends at each level. A cell holds two lines on a saddle, entering by the sides 0
  // and 2, or 1 and 3.
  private byte[] takenBy = [];

  // The level being traced, which each call to Contours moves on.
  private byte stamp;

  /// <summary>
  /// The greatest distance of the corners of the cells inside the outline.
  /// </summary>
  internal float Highest { get; private set; }

  /// <summary>
  /// The lines at a distance from the toe, clipped to the outline.
  /// </summary>
  internal List<RowLine> Contours(float level) {
    if (++this.stamp == 0) {
      Array.Clear(this.takenBy, 0, this.takenBy.Length);

      this.stamp = 1;
    }

    var lines = new List<RowLine>();
    var last = Math.Min((int) (level / this.cell), this.folds.Length - 1);

    // A cell's corners are at most its diagonal apart in distance.
    var first = Math.Max(0, (int) ((level / this.cell) - 1.42f));

    for (var bucket = first; bucket <= last; bucket++) {
      for (var k = this.buckets[bucket]; k < this.buckets[bucket + 1]; k++) {
        this.AddLines(this.cells[k], level, lines);
      }
    }

    return lines;
  }

  /// <summary>
  /// Whether the field folds between two levels, where two sides of the breakwater meet.
  /// </summary>
  internal bool HasFold(float from, float to) {
    var last = Math.Min((int) (to / this.cell), this.folds.Length - 1);

    for (var bucket = Math.Max(0, (int) (from / this.cell) - 1); bucket <= last; bucket++) {
      if (this.folds[bucket]) {
        return true;
      }
    }

    return false;
  }

  /// <summary>
  /// Sorts the cells inside the outline into buckets by their least distance, and marks the
  /// distances at which the field folds.
  /// </summary>
  private void SortCells() {
    this.states = new CellState[this.distances.Length];
    this.takenBy = new byte[2 * this.distances.Length];

    // As far as the grid's corners are apart, in cells: no bucket is past it.
    this.folds = new bool[this.countX + this.countZ];
    this.buckets = new int[this.folds.Length + 1];

    var rows = this.MarkOutline();
    var inside = new int[rows.Sum(static row => Math.Max(0, row.Last - row.First + 1))];
    var bucketOf = new int[inside.Length];
    var count = 0;

    for (var j = 0; j < rows.Length; j++) {
      var state = CellState.Outside;

      for (var i = rows[j].First; i <= rows[j].Last; i++) {
        var node = (j * this.countX) + i;

        // [PERF] One test for all the cells between two that the outline crosses, all inside or all
        // outside, in place of one per cell.
        if (this.states[node] is not CellState.Crossed) {
          if (this.states[node - 1] is CellState.Crossed) {
            state = this.Contains(this.Node(node) + (0.5f * this.cell))
              ? CellState.Inside
              : CellState.Outside;
          }

          this.states[node] = state;
        }

        if (this.states[node] is not CellState.Outside) {
          inside[count] = node;
          bucketOf[count] = this.Measure(node);
          count++;
        }
      }
    }

    // Each bucket's start among the sorted cells, from the cells counted one place further.
    for (var bucket = 1; bucket < this.buckets.Length; bucket++) {
      this.buckets[bucket] += this.buckets[bucket - 1];
    }

    this.cells = new int[count];

    var next = (int[]) this.buckets.Clone();

    for (var k = 0; k < count; k++) {
      this.cells[next[bucketOf[k]]++] = inside[k];
    }
  }

  /// <summary>
  /// Counts a cell in its bucket, which it returns, in the folds, and in the highest distance.
  /// </summary>
  private int Measure(int node) {
    var a = this.distances[node];
    var b = this.distances[node + 1];
    var c = this.distances[node + this.countX];
    var d = this.distances[node + this.countX + 1];
    var least = Math.Min(Math.Min(a, b), Math.Min(c, d));
    var most = Math.Max(Math.Max(a, b), Math.Max(c, d));
    var bucket = (int) (least / this.cell);

    if (most > this.Highest) {
      this.Highest = most;
    }

    // The gradient at the cell's first corner, two cells long: across a single cell, it is as steep
    // as anywhere when the fold runs along the cell's side.
    var slopeX = b - this.distances[node - 1];
    var slopeZ = c - this.distances[node - this.countX];
    var fold = 2f * this.cell * ToeField.FoldGradient;
    var level = (int) (a / this.cell);

    // Not the toe itself, in the first bucket: the distance rises on both its sides.
    if (level > 0 && (slopeX * slopeX) + (slopeZ * slopeZ) < fold * fold) {
      this.folds[level] = true;
    }

    this.buckets[bucket + 1]++;

    return bucket;
  }

  /// <summary>
  /// Marks the cells the outline crosses, and returns the first and last column of each row's, the
  /// first past the last in a row it misses.
  /// </summary>
  private (int First, int Last)[] MarkOutline() {
    var rows = new (int First, int Last)[this.countZ];

    for (var j = 0; j < rows.Length; j++) {
      rows[j] = (this.countX, -1);
    }

    for (var k = 0; k < this.outline.Length; k++) {
      var a = (this.outline[k] - this.origin) / this.cell;
      var b = (this.outline[(k + 1) % this.outline.Length] - this.origin) / this.cell;

      if (a.y > b.y) {
        (a, b) = (b, a);
      }

      for (var j = (int) a.y; j <= (int) b.y; j++) {
        // The part of the edge in this row of cells.
        var u = math.saturate((new float2(j, j + 1) - a.y) / math.max(b.y - a.y, 1e-6f));
        var x = math.lerp(a.x, b.x, u);
        var first = (int) math.cmin(x);
        var last = (int) math.cmax(x);

        for (var i = first; i <= last; i++) {
          this.states[(j * this.countX) + i] = CellState.Crossed;
        }

        rows[j] = (Math.Min(rows[j].First, first), Math.Max(rows[j].Last, last));
      }
    }

    return rows;
  }

  /// <summary>
  /// The point of a node, and of a cell's corner of least x and z.
  /// </summary>
  private float2 Node(int node) {
    var z = node / this.countX;

    return this.origin + (new float2(node - (z * this.countX), z) * this.cell);
  }

  /// <summary>
  /// The side by which a level's line leaves a cell that it enters by a side, the uphill on its
  /// left, or -1 where no line enters there. The mask holds which corners are at the level or
  /// higher, and corner and side 0 are the ones of least x and z, the others counterclockwise: a
  /// line enters where the field falls from a corner to the next, and leaves where it next rises.
  /// A saddle's two lines cut off its low corners.
  /// </summary>
  private static int LeaveOf(int mask, int enter) {
    if (!IsHigh(enter) || IsHigh(enter + 1)) {
      return -1;
    }

    var leave = enter + 1;

    while (IsHigh(leave) || !IsHigh(leave + 1)) {
      leave++;
    }

    return leave % 4;

    bool IsHigh(int corner) => (mask & (1 << (corner % 4))) != 0;
  }

  /// <summary>
  /// Which of a cell's corners are at a level or higher.
  /// </summary>
  private int Mask(int node, float level) =>
    (this.distances[node] >= level ? 1 : 0) |
    (this.distances[node + 1] >= level ? 2 : 0) |
    (this.distances[node + this.countX + 1] >= level ? 4 : 0) |
    (this.distances[node + this.countX] >= level ? 8 : 0);

  /// <summary>
  /// Adds the lines of a level that cross a cell, each followed to both its ends.
  /// </summary>
  private void AddLines(int node, float level, List<RowLine> lines) {
    var mask = this.Mask(node, level);

    if (mask is 0 or 15) {
      return;
    }

    for (var side = 0; side < 4; side++) {
      if (ToeField.Leaves[(4 * mask) + side] >= 0 && !this.IsTaken(node, side)) {
        this.Trace(node, side, level, lines);
      }
    }
  }

  /// <summary>
  /// Adds the parts inside the outline of the line that enters a cell by a side, taking its cells.
  /// </summary>
  private void Trace(int node, int side, float level, List<RowLine> lines) {
    var start = node;
    var enter = side;
    var isLoop = false;

    // Back to where the line starts, or round a loop to the cell again. The side a line leaves a
    // cell by is the next cell's opposite one, 2 places round. The grid's margin keeps the cells
    // inside off its edge.
    while (!isLoop && this.states[this.Across(start, enter)] is not CellState.Outside) {
      start = this.Across(start, enter);
      enter = ToeField.Enters[(4 * this.Mask(start, level)) + (enter ^ 2)];
      isLoop = start == node && enter == side;
    }

    var pieces = new List<List<float2>>();
    var from = this.Crossing(start, enter, level);

    while (this.states[start] is not CellState.Outside && !this.IsTaken(start, enter)) {
      this.Take(start, enter);

      var leave = ToeField.Leaves[(4 * this.Mask(start, level)) + enter];
      var to = this.Crossing(start, leave, level);

      if (this.states[start] is CellState.Inside) {
        ToeField.Add(from, to, pieces);
      }
      else {
        this.Clip(from, to, pieces);
      }

      from = to;
      start = this.Across(start, leave);
      enter = leave ^ 2;
    }

    // A loop's last piece goes on into its first.
    if (isLoop && pieces.Count > 1 && pieces[^1][^1].Equals(pieces[0][0])) {
      pieces[^1].AddRange(pieces[0].Skip(1));
      pieces.RemoveAt(0);
    }

    foreach (var piece in pieces) {
      var isClosed = piece[^1].Equals(piece[0]);

      if (isClosed) {
        piece.RemoveAt(piece.Count - 1);
      }

      lines.Add(new RowLine([.. piece], isClosed));
    }
  }

  private bool IsTaken(int node, int enter) =>
    this.takenBy[(2 * node) + (enter >> 1)] == this.stamp;

  private void Take(int node, int enter) => this.takenBy[(2 * node) + (enter >> 1)] = this.stamp;

  /// <summary>
  /// The cell across a side of a cell.
  /// </summary>
  private int Across(int node, int side) =>
    node +
    side switch {
      0 => -this.countX,
      1 => 1,
      2 => this.countX,
      _ => -1
    };

  /// <summary>
  /// Where a level crosses a side of a cell.
  /// </summary>
  private float2 Crossing(int node, int side, float level) {
    var from = side switch {
      1 => node + 1,
      2 => node + this.countX,
      _ => node
    };

    var alongX = side % 2 == 0;
    var a = this.distances[from];
    var b = this.distances[alongX ? from + 1 : from + this.countX];
    var offset = math.saturate((level - a) / (b - a)) * this.cell;

    return this.Node(from) + (alongX ? new float2(offset, 0f) : new float2(0f, offset));
  }

  /// <summary>
  /// Adds the parts of a segment inside the outline to a line's pieces.
  /// </summary>
  private void Clip(float2 from, float2 to, List<List<float2>> pieces) {
    var along = to - from;
    var cuts = new List<float>();
    var cornerX = this.outline[^1].x - from.x;
    var cornerZ = this.outline[^1].y - from.y;

    // [PERF] Spelled out, in place of cross products of float2, calls Mono does not inline.
    foreach (var point in this.outline) {
      var nextX = point.x - from.x;
      var nextZ = point.y - from.y;
      var edgeX = nextX - cornerX;
      var edgeZ = nextZ - cornerZ;
      var cross = (along.x * edgeZ) - (along.y * edgeX);
      var cutCross = (cornerX * edgeZ) - (cornerZ * edgeX);
      var edgeCross = (cornerX * along.y) - (cornerZ * along.x);
      var cut = cutCross / cross;
      var onEdge = edgeCross / cross;

      // None where they are parallel, both infinite or not numbers.
      if (cut is > 0f and < 1f && onEdge is >= 0f and <= 1f) {
        cuts.Add(cut);
      }

      cornerX = nextX;
      cornerZ = nextZ;
    }

    cuts.Sort();

    var start = from;

    for (var k = 0; k <= cuts.Count; k++) {
      var end = k < cuts.Count ? from + (cuts[k] * along) : to;

      if (this.Contains((start + end) * 0.5f)) {
        ToeField.Add(start, end, pieces);
      }

      start = end;
    }
  }

  /// <summary>
  /// Adds a segment to a line's last piece where it goes on from it, or to a new one.
  /// </summary>
  private static void Add(float2 from, float2 to, List<List<float2>> pieces) {
    if (from.Equals(to)) {
      return;
    }

    if (pieces.Count > 0 && pieces[^1][^1].Equals(from)) {
      pieces[^1].Add(to);
    }
    else {
      pieces.Add([from, to]);
    }
  }

  /// <summary>
  /// What the outline holds of a cell: nothing, a part, or all.
  /// </summary>
  private enum CellState : byte {
    Outside,

    Crossed,

    Inside
  }
}
