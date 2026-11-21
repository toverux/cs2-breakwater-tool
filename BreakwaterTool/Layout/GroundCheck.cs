using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace BreakwaterTool.Layout;

/// <summary>
/// Whether the ground inside an outline holds a mound to dress: the tool dresses terrain and never
/// shapes it, so a player who drew over level ground or open water is told to shape it first.
/// </summary>
internal static class GroundCheck {
  /// <summary>
  /// The relief under which the ground inside an outline is level, in metres.
  /// </summary>
  internal const float LevelRelief = 2f;

  /// <summary>
  /// The water depth over which a point is under water, in metres, above the film of water a
  /// shore's terrain carries.
  /// </summary>
  internal const float WetDepth = 0.1f;

  // Samples per side of the outline's bounds: enough for a mound.
  private const int GridSide = 16;

  /// <param name="heightAt">The terrain's height at a point.</param>
  /// <param name="depthAt">
  /// The water's depth over the terrain at a point, zero where it is dry.
  /// </param>
  internal static GroundVerdict Judge(
    IReadOnlyList<float2> polygon,
    Func<float2, float> heightAt,
    Func<float2, float> depthAt
  ) {
    var samples = GroundCheck.Samples(polygon).ToList();

    if (samples.Count == 0) {
      return GroundVerdict.Shaped;
    }

    if (samples.All(point => depthAt(point) > GroundCheck.WetDepth)) {
      return GroundVerdict.Underwater;
    }

    var heights = samples.Select(heightAt).ToList();

    return heights.Max() - heights.Min() < GroundCheck.LevelRelief
      ? GroundVerdict.Level
      : GroundVerdict.Shaped;
  }

  /// <summary>
  /// Whether a point is inside a polygon, by the even-odd rule.
  /// </summary>
  internal static bool Contains(IReadOnlyList<float2> polygon, float2 point) {
    var inside = false;

    for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++) {
      var a = polygon[i];
      var b = polygon[j];

      if ((a.y > point.y) != (b.y > point.y) &&
        point.x < a.x + ((point.y - a.y) * (b.x - a.x) / (b.y - a.y))) {
        inside = !inside;
      }
    }

    return inside;
  }

  /// <summary>
  /// The outline's vertices, and the points of a grid over its bounds that fall inside it.
  /// </summary>
  private static IEnumerable<float2> Samples(IReadOnlyList<float2> polygon) {
    if (polygon.Count < 3) {
      yield break;
    }

    foreach (var vertex in polygon) {
      yield return vertex;
    }

    var low = polygon.Aggregate(math.min);
    var high = polygon.Aggregate(math.max);
    var step = (high - low) / GroundCheck.GridSide;

    for (var i = 0; i <= GroundCheck.GridSide; i++) {
      for (var j = 0; j <= GroundCheck.GridSide; j++) {
        var point = low + (step * new float2(i, j));

        if (GroundCheck.Contains(polygon, point)) {
          yield return point;
        }
      }
    }
  }
}

internal enum GroundVerdict {
  /// <summary>
  /// The ground holds relief out of the water.
  /// </summary>
  Shaped,

  /// <summary>
  /// The ground inside the outline is level.
  /// </summary>
  Level,

  /// <summary>
  /// The ground inside the outline is wholly under water.
  /// </summary>
  Underwater
}
