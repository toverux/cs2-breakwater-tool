using System.Collections.Generic;
using Unity.Mathematics;

namespace BreakwaterTool.Layout;

/// <summary>
/// Points, to find the ones within a reach of a point.
/// </summary>
internal sealed class SpatialIndex(float reach) {
  // The reach round a point spans two cells along x, and two along z.
  private readonly float cell = 2f * reach;

  private readonly List<float2> points = [];

  // [PERF] A cell's points chained through arrays, in place of a dictionary of lists.
  private readonly List<int> nexts = [];

  // Per hashed cell, its last point's number, and nothing at 0; grown as points are added.
  private int[] heads = new int[256];

  internal IReadOnlyList<float2> Points => this.points;

  internal void Add(float2 point) {
    if (this.points.Count * 2 > this.heads.Length) {
      this.heads = new int[this.heads.Length * 4];

      for (var i = 0; i < this.points.Count; i++) {
        this.Link(i);
      }
    }

    this.points.Add(point);
    this.nexts.Add(0);
    this.Link(this.points.Count - 1);
  }

  /// <summary>
  /// Puts into a list the points within the reach of a point; one may come twice, where two cells
  /// share a hash.
  /// </summary>
  internal void Near(float2 point, List<float2> near) {
    var x = this.Column(point.x - reach);
    var z = this.Column(point.y - reach);

    near.Clear();

    for (var k = 0; k < 4; k++) {
      var number = this.heads[this.Slot(x + (k & 1), z + (k >> 1))];

      while (number > 0) {
        // [PERF] Spelled out, in place of math.distancesq, a call Mono does not inline.
        var found = this.points[number - 1];
        var offsetX = found.x - point.x;
        var offsetZ = found.y - point.y;

        if ((offsetX * offsetX) + (offsetZ * offsetZ) < reach * reach) {
          near.Add(found);
        }

        number = this.nexts[number - 1];
      }
    }
  }

  private void Link(int index) {
    var point = this.points[index];
    var slot = this.Slot(this.Column(point.x), this.Column(point.y));

    this.nexts[index] = this.heads[slot];
    this.heads[slot] = index + 1;
  }

  private int Column(float coordinate) => (int) math.floor(coordinate / this.cell);

  private int Slot(int x, int z) =>
    (int) ((uint) ((x * 73856093) ^ (z * 19349663)) & (uint) (this.heads.Length - 1));
}
