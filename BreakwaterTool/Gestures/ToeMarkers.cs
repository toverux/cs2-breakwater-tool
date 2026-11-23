using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace BreakwaterTool.Gestures;

/// <summary>
/// The handle floating outside the middle of an edge of the picked breakwater, which a press makes
/// toe or not.
/// </summary>
/// <param name="At">Its middle.</param>
/// <param name="Radius">
/// How far from its middle it shows and answers the pointer, in metres.
/// </param>
internal readonly record struct ToeMarker(float3 At, float Radius);

/// <summary>
/// Where the toe markers of an outline float, and which one the pointer is on.
/// </summary>
internal static class ToeMarkers {
  // How far outside its edge a marker floats and how far it reaches, in pointer reaches: as wide
  // as WCAG's minimum target, and clear of the reach of its own edge.
  private const float Offset = 2.5f;

  private const float Reach = 1f;

  /// <summary>
  /// A marker per edge of an outline, edge i from point i to i + 1: outside the outline, beside the
  /// edge's middle, at the height of the edge there or on the ground where the ground is higher.
  /// </summary>
  /// <param name="heightAt">The terrain's height at a point.</param>
  /// <param name="reachAt">The pointer's reach at a point, in metres, which sizes a marker.</param>
  internal static ToeMarker[] Of(
    IReadOnlyList<float2> outline,
    Func<float2, float> heightAt,
    Func<float3, float> reachAt
  ) {
    // Twice the outline's area, positive when it turns counter-clockwise seen from above: outside
    // is then on the right of each edge.
    var turn = 0f;

    for (var i = 0; i < outline.Count; i++) {
      var from = outline[i];
      var to = outline[(i + 1) % outline.Count];

      turn += (from.x * to.y) - (to.x * from.y);
    }

    var markers = new ToeMarker[outline.Count];

    for (var i = 0; i < outline.Count; i++) {
      var a = outline[i];
      var b = outline[(i + 1) % outline.Count];
      var middle = math.lerp(a, b, 0.5f);
      var edgeHeight = math.lerp(heightAt(a), heightAt(b), 0.5f);
      var along = math.normalizesafe(b - a);
      var outside = new float2(along.y, -along.x) * math.sign(turn);
      var reach = reachAt(new float3(middle.x, edgeHeight, middle.y));
      var at = middle + (outside * reach * ToeMarkers.Offset);

      markers[i] = new ToeMarker(
        new float3(at.x, math.max(edgeHeight, heightAt(at)), at.y),
        reach * ToeMarkers.Reach
      );
    }

    return markers;
  }

  /// <summary>
  /// The marker the pointer is on: the one its ray passes nearest, within the marker's radius, or
  /// -1. The ray runs from the eye through a point and past it, so a marker answers where it shows
  /// on screen, in front of the ground or seen through it.
  /// </summary>
  internal static int Aimed(IReadOnlyList<ToeMarker> markers, float3 eye, float3 through) {
    var ray = math.normalizesafe(through - eye);
    var aimed = -1;
    var nearest = 1f;

    for (var i = 0; i < markers.Count; i++) {
      var (at, radius) = markers[i];
      var depth = math.max(0f, math.dot(at - eye, ray));
      var share = math.distance(eye + (depth * ray), at) / radius;

      if (share < nearest) {
        aimed = i;
        nearest = share;
      }
    }

    return aimed;
  }
}
