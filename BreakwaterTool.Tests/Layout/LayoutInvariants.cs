using System.Collections.Generic;
using System.Linq;
using BreakwaterTool.Layout;
using BreakwaterTool.Scenarios.Ground;
using Unity.Mathematics;

namespace BreakwaterTool.Tests.Layout;

/// <summary>
/// What holds of any layout that lays stones: no bare ground inside the outline, no two stones
/// closer than half the smaller spacing, and no stone outside the outline.
/// </summary>
internal static class LayoutInvariants {
  /// <summary>
  /// The most room a layout leaves, in rooms of an even bond: over one for the irregularity, the
  /// corners of rows, and the seams between them.
  /// </summary>
  private const float MostRooms = 1.45f;

  /// <summary>
  /// The first invariant the layout breaks and where, or null when all hold or nothing was laid.
  /// The layout must hold a single layer, as a second one lies between the stones of the first.
  /// </summary>
  internal static string? Breach(
    IReadOnlyList<float2> outline,
    LayoutResult result,
    IReadOnlyList<StoneShape> shapes,
    BreakwaterParameters parameters
  ) {
    if (result.Verdict is not LayoutVerdict.Laid) {
      return null;
    }

    var (rooms, barest) = LayoutInvariants.Barest(outline, result, shapes, parameters);

    if (rooms > LayoutInvariants.MostRooms) {
      return $"A point {rooms:F2} rooms of an even bond from any stone, at {barest}.";
    }

    var centres = LayoutInvariants.Centres(result, shapes, parameters);

    var smaller = math.min(result.StoneSpacing, result.RowSpacing);
    var least = 0.5f * smaller;
    var (closest, first, second) = LayoutInvariants.ClosestPair(centres, least);

    if (closest < least) {
      return $"Two stones {closest:F2} m apart, at {first} and {second}, under {least:F2} m.";
    }

    // A place is inside, but for the rounding of a row round a corner of the outline; a stone is
    // off its place by up to its irregularity each way.
    var most = math.min(0.5f, 0.25f * smaller) +
      (parameters.Irregularity * result.StoneSpacing * math.SQRT2);

    var outside = centres.Max(centre => LayoutInvariants.Outside(centre, outline));

    return outside > most ? $"A stone {outside:F2} m outside, over {most:F2} m." : null;
  }

  /// <summary>
  /// The point inside the outline the farthest from every stone, and how far it is, in rooms: the
  /// most that a point of an even bond of the layout's spacings is from its stones.
  /// The band along the outline's edges off the toe is left out, where the last row stops short of
  /// the crest; along the toe, whose row is on it, nothing is.
  /// </summary>
  internal static (float Rooms, float2 Point) Barest(
    IReadOnlyList<float2> outline,
    LayoutResult result,
    IReadOnlyList<StoneShape> shapes,
    BreakwaterParameters parameters
  ) {
    var (s, r) = (result.StoneSpacing, result.RowSpacing);

    // The circle through a stone, the next one of its row, and the one between them of the next, or
    // through the nearest three of three rows where the rows are closer than that.
    var room = ((s * s / 4f) + (r * r)) / math.max(2f * r, s);
    var reach = 2f * room;

    // The stones bucketed by cells of the reach, so a point only looks at the nine around it.
    var buckets = LayoutInvariants.Centres(result, shapes, parameters)
      .GroupBy(centre => (int2) math.floor(centre / reach))
      .ToDictionary(static group => group.Key, static group => group.ToArray());

    var min = outline.Aggregate(math.min);
    var max = outline.Aggregate(math.max);

    // Fine enough to read a hole to a tenth of a room, but on an outline of kilometres.
    var step = math.max(0.15f * room, math.cmax(max - min) / 500f);
    var barest = (Distance: 0f, Point: float2.zero);

    for (var x = min.x; x <= max.x; x += step) {
      for (var z = min.y; z <= max.y; z += step) {
        var point = new float2(x, z);

        if (!LayoutInvariants.IsInside(point, outline)) {
          continue;
        }

        var distance = Nearest(point, barest.Distance);

        if (distance > barest.Distance &&
          LayoutInvariants.EdgeDistance(point, outline, result.ToeEdges) >= math.max(s, r)) {
          barest = (distance, point);
        }
      }
    }

    return (barest.Distance / room, barest.Point);

    // The distance to the nearest stone, or to the first one found nearer than is asked.
    float Nearest(float2 point, float than) {
      var cell = (int2) math.floor(point / reach);
      var nearest = reach;

      for (var i = -1; i <= 1; i++) {
        for (var j = -1; j <= 1; j++) {
          foreach (var centre in buckets.GetValueOrDefault(cell + new int2(i, j), [])) {
            nearest = math.min(nearest, math.distance(centre, point));

            if (nearest <= than) {
              return nearest;
            }
          }
        }
      }

      return nearest;
    }
  }

  private static float2[] Centres(
    LayoutResult result,
    IReadOnlyList<StoneShape> shapes,
    BreakwaterParameters parameters
  ) => [
    .. result.Stones.Select(stone =>
      LayoutPicture.Centre(stone, shapes[stone.Shape], parameters.Embed)
    )
  ];

  /// <summary>
  /// The nearest two points of a set and their distance, over the pairs closer than a bound; the
  /// distance is infinite when no pair is closer.
  /// </summary>
  private static (float Distance, float2 First, float2 Second) ClosestPair(
    IReadOnlyList<float2> points,
    float bound
  ) {
    // Buckets of the bound: a point closer than the bound to another lies in the nine around it.
    var buckets = new Dictionary<int2, List<float2>>();
    var closest = (Distance: float.PositiveInfinity, First: float2.zero, Second: float2.zero);

    foreach (var point in points) {
      var cell = (int2) math.floor(point / bound);

      for (var i = -1; i <= 1; i++) {
        for (var j = -1; j <= 1; j++) {
          if (!buckets.TryGetValue(cell + new int2(i, j), out var near)) {
            continue;
          }

          foreach (var other in near) {
            var distance = math.distance(point, other);

            if (distance < closest.Distance) {
              closest = (distance, point, other);
            }
          }
        }
      }

      if (!buckets.TryGetValue(cell, out var own)) {
        buckets[cell] = own = [];
      }

      own.Add(point);
    }

    return closest;
  }

  /// <summary>
  /// How far a point lies from the nearest of a polygon's edges, infinite when all are left out.
  /// </summary>
  /// <param name="leftOut">Per edge, whether it is left out; empty for none.</param>
  private static float EdgeDistance(
    float2 point,
    IReadOnlyList<float2> polygon,
    IReadOnlyList<bool> leftOut
  ) {
    var nearest = float.PositiveInfinity;

    for (var i = 0; i < polygon.Count; i++) {
      if (leftOut.ElementAtOrDefault(i)) {
        continue;
      }

      var a = polygon[i];
      var ab = polygon[(i + 1) % polygon.Count] - a;
      var u = math.saturate(math.dot(point - a, ab) / math.max(math.lengthsq(ab), 1e-6f));

      nearest = math.min(nearest, math.distance(point, a + (u * ab)));
    }

    return nearest;
  }

  /// <summary>
  /// How far a point lies outside a polygon, or zero inside it.
  /// </summary>
  private static float Outside(float2 point, IReadOnlyList<float2> polygon) =>
    LayoutInvariants.IsInside(point, polygon)
      ? 0f
      : LayoutInvariants.EdgeDistance(point, polygon, []);

  private static bool IsInside(float2 point, IReadOnlyList<float2> polygon) {
    var inside = false;

    for (var i = 0; i < polygon.Count; i++) {
      var a = polygon[i];
      var b = polygon[(i + 1) % polygon.Count];

      if ((a.y > point.y) != (b.y > point.y) &&
        point.x < a.x + ((point.y - a.y) / (b.y - a.y) * (b.x - a.x))) {
        inside = !inside;
      }
    }

    return inside;
  }
}
