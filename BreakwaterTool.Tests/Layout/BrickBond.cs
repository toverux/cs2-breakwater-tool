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
  internal const float MostBroken = 0.08f;

  /// <summary>
  /// How far off the middle of the two stones below it, in stone spacings, a stone is out of the
  /// bond; half a spacing off, it stands right on one of them.
  /// </summary>
  internal const float Broken = 0.3f;

  /// <summary>
  /// How much farther than its nearest toe edge, in row spacings, another toe edge may be from a
  /// stone that closes the rows of both: between two sides that face each other, a stone half a row
  /// spacing short of the fold where their rows meet is a row spacing nearer one than the other.
  /// </summary>
  private const float FoldReach = 1f;

  /// <summary>
  /// The share of the stones out of the bond, among those with a stone of the row below on each
  /// side.
  /// </summary>
  internal static float BrokenShare(
    IReadOnlyList<float2> outline,
    LayoutResult result,
    IReadOnlyList<StoneShape> shapes,
    BreakwaterParameters parameters
  ) =>
    BrickBond.Share(BrickBond.Judge(outline, result, shapes, parameters));

  /// <summary>
  /// The share of the stones out of the bond, as <see cref="BrokenShare"/>, left out those of the
  /// fans: round a toe's end or inner corner, each row is longer than the one below by a stone in
  /// so many, and a stone gained sits on one below it.
  /// </summary>
  internal static float BrokenShareOutsideFans(
    IReadOnlyList<float2> outline,
    LayoutResult result,
    IReadOnlyList<StoneShape> shapes,
    BreakwaterParameters parameters
  ) =>
    BrickBond.Share(
      BrickBond.Judge(outline, result, shapes, parameters).Where(static stone => !stone.IsInFan)
    );

  /// <summary>
  /// Each stone with a stone of its own side's row below on each side, and how far off the bond it
  /// is. Near a fold, a stone closes the rows of each side it meets, so it is off the bond only
  /// when it is off every one of them.
  /// </summary>
  internal static IEnumerable<JudgedStone> Judge(
    IReadOnlyList<float2> outline,
    LayoutResult result,
    IReadOnlyList<StoneShape> shapes,
    BreakwaterParameters parameters
  ) {
    var centres = result.Stones
      .Select(stone => LayoutPicture.Centre(stone, shapes[stone.Shape], parameters.Embed))
      .ToArray();

    var toe = Enumerable.Range(0, outline.Count).Where(i => result.ToeEdges[i]).ToArray();

    // Each stone's way from the nearest point of each toe edge.
    var ways = centres
      .Select(centre => toe.Select(edge => BrickBond.FromEdge(centre, outline, edge)).ToArray())
      .ToArray();

    // As far as two stones of the row below may stand from the one between them.
    var reach = math.length(new float2(result.StoneSpacing, result.RowSpacing));

    for (var stone = 0; stone < centres.Length; stone++) {
      var distances = ways[stone].Select(static way => math.length(way.Uphill)).ToArray();
      var own = Array.IndexOf(distances, distances.Min());

      if (Off(stone, own) is not {} off) {
        continue;
      }

      for (var side = 0; side < toe.Length; side++) {
        if (side != own &&
          distances[side] <= distances[own] + (BrickBond.FoldReach * result.RowSpacing) &&
          Off(stone, side) is {} other) {
          off = math.min(off, other);
        }
      }

      yield return new JudgedStone(centres[stone], off, ways[stone][own].IsAtVertex);
    }

    // How far off the middle of the two stones of a side's row below it a stone is, in stone
    // spacings; null where that row has no stone on each side of it.
    float? Off(int stone, int side) {
      var uphill = ways[stone][side].Uphill;
      var distance = math.length(uphill);
      var along = math.normalizesafe(new float2(-uphill.y, uphill.x));

      // The row below is half a row spacing to a whole one away, nearer the steeper the ground
      // is, and downhill: across the spine, rows as low are another side's.
      var below = Enumerable.Range(0, centres.Length)
        .Where(other =>
          (distance - math.length(ways[other][side].Uphill)) / result.RowSpacing is > 0.45f and
          < 1.3f &&
          math.distance(centres[stone], centres[other]) < reach &&
          math.dot(centres[other] - centres[stone], uphill) < 0f
        )
        .Select(other => centres[other] - centres[stone])
        .OrderBy(offset => math.dot(offset, along))
        .ToArray();

      var ahead = Array.FindIndex(below, offset => math.dot(offset, along) > 0f);

      if (ahead < 1) {
        return null;
      }

      var gap = math.dot(below[ahead] - below[ahead - 1], along);

      if (gap / result.StoneSpacing is not (> 0.5f and < 1.5f)) {
        return null;
      }

      var off = (math.lengthsq(below[ahead]) - math.lengthsq(below[ahead - 1])) / (2f * gap);

      return math.abs(off) / result.StoneSpacing;
    }
  }

  private static float Share(IEnumerable<JudgedStone> judged) {
    var offs = judged.Select(static stone => stone.Off).ToArray();

    return (float) offs.Count(static off => off > BrickBond.Broken) / offs.Length;
  }

  /// <summary>
  /// The way to a point from a toe edge's nearest point, and whether that point is an end of the
  /// edge, where the rows turn round it.
  /// </summary>
  private static (float2 Uphill, bool IsAtVertex) FromEdge(
    float2 point,
    IReadOnlyList<float2> outline,
    int edge
  ) {
    var a = outline[edge];
    var ab = outline[(edge + 1) % outline.Count] - a;
    var u = math.saturate(math.dot(point - a, ab) / math.lengthsq(ab));

    return (point - a - (u * ab), u is 0f or 1f);
  }
}

/// <summary>
/// A stone judged against the two stones of the row below it.
/// </summary>
/// <param name="Centre">Its footprint's centre.</param>
/// <param name="Off">How far off the middle of the two, in stone spacings, at the least.</param>
/// <param name="IsInFan">Whether the toe's nearest point to it is an end of a toe edge.</param>
internal readonly record struct JudgedStone(float2 Centre, float Off, bool IsInFan);
