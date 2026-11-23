using System;
using System.Collections.Generic;
using System.Linq;
using BreakwaterTool.Layout;
using BreakwaterTool.Scenarios.Ground;
using Unity.Mathematics;

namespace BreakwaterTool.Tests.Layout;

/// <summary>
/// How well a layout keeps the brick bond, read from where its stones are.
/// </summary>
internal static class BrickBond {
  /// <summary>
  /// The share of stones out of the bond that a layout may hold: a few are, where rows fan out,
  /// close up, or meet.
  /// </summary>
  internal const float MostBroken = 0.1f;

  /// <summary>
  /// How far off the middle of the two stones below it, in stone spacings, a stone is out of the
  /// bond; half a spacing off, it stands right on one of them.
  /// </summary>
  private const float Broken = 0.3f;

  /// <summary>
  /// The share of the stones out of the bond, among those with a stone of the row below on each
  /// side.
  /// </summary>
  internal static float BrokenShare(
    IReadOnlyList<float2> outline,
    LayoutResult result,
    IReadOnlyList<StoneShape> shapes,
    BreakwaterParameters parameters
  ) {
    var stones = result.Stones
      .Select(stone => LayoutPicture.Centre(stone, shapes[stone.Shape], parameters.Embed))
      .Select(centre =>
        (Centre: centre, Uphill: BrickBond.FromToe(centre, outline, result.ToeEdges))
      )
      .Select(static stone => (stone.Centre, stone.Uphill, Distance: math.length(stone.Uphill)))
      .ToArray();

    // As far as two stones of the row below may stand from the one between them.
    var reach = math.length(new float2(result.StoneSpacing, result.RowSpacing));
    var offs = new List<float>();

    foreach (var stone in stones) {
      var along = math.normalizesafe(new float2(-stone.Uphill.y, stone.Uphill.x));

      // The row below is half a row spacing to a whole one away, nearer the steeper the ground is,
      // and downhill: across the spine, rows as low are another side's.
      var below = stones
        .Where(other =>
          (stone.Distance - other.Distance) / result.RowSpacing is > 0.45f and < 1.3f &&
          math.distance(stone.Centre, other.Centre) < reach &&
          math.dot(other.Centre - stone.Centre, stone.Uphill) < 0f
        )
        .Select(other => other.Centre - stone.Centre)
        .OrderBy(offset => math.dot(offset, along))
        .ToArray();

      var ahead = Array.FindIndex(below, offset => math.dot(offset, along) > 0f);

      if (ahead < 1) {
        continue;
      }

      var gap = math.dot(below[ahead] - below[ahead - 1], along);

      if (gap / result.StoneSpacing is > 0.5f and < 1.5f) {
        var off = (math.lengthsq(below[ahead]) - math.lengthsq(below[ahead - 1])) / (2f * gap);

        offs.Add(math.abs(off) / result.StoneSpacing);
      }
    }

    return (float) offs.Count(static off => off > BrickBond.Broken) / offs.Count;
  }

  /// <summary>
  /// The way to a point from the toe's nearest one.
  /// </summary>
  private static float2 FromToe(
    float2 point,
    IReadOnlyList<float2> outline,
    IReadOnlyList<bool> toe
  ) {
    var nearest = new float2(float.PositiveInfinity);

    for (var i = 0; i < outline.Count; i++) {
      if (!toe[i]) {
        continue;
      }

      var a = outline[i];
      var ab = outline[(i + 1) % outline.Count] - a;
      var u = math.saturate(math.dot(point - a, ab) / math.lengthsq(ab));
      var from = point - a - (u * ab);

      nearest = math.lengthsq(from) < math.lengthsq(nearest) ? from : nearest;
    }

    return nearest;
  }
}
