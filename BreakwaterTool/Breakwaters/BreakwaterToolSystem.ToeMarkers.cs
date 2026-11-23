using BreakwaterTool.Gestures;
using Colossal.Mathematics;
using Game.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace BreakwaterTool.Breakwaters {
  /// <summary>
  /// The toe markers of the picked breakwater: a handle floating outside the middle of each edge,
  /// which a click makes toe or not. The layout chooses the toe until the first click.
  /// </summary>
  internal sealed partial class BreakwaterToolSystem {
    // The picked breakwater's markers this frame, one per edge; empty while it shows none.
    private ToeMarker[] toeMarkers = [];

    /// <summary>
    /// Places the markers for the frame, before the pointer is read against them.
    /// </summary>
    private void ReadToeMarkers() =>
      this.toeMarkers = this.gestures.HasToeMarkers
        ? ToeMarkers.Of(this.gestures.Points, this.HeightAt, this.view.ReachAt)
        : [];

    /// <summary>
    /// Draws each edge's marker: a disc in the toe's colour on a toe edge, a ring on another, and
    /// brighter under the pointer; a stem ties it to the middle of its edge.
    /// </summary>
    private void DrawToeMarkers(
      OverlayRenderSystem.Buffer buffer,
      GestureTarget<Entity> target,
      (Color Toe, Color Edge, Color Aimed) colors
    ) {
      if (this.toeMarkers.Length != this.toe.Length) {
        return;
      }

      var points = this.gestures.Points;

      for (var edge = 0; edge < this.toeMarkers.Length; edge++) {
        var (at, radius) = this.toeMarkers[edge];
        var isToe = this.toe[edge];
        var isAimed = target.Kind is TargetKind.ToeMarker && target.Index == edge;
        var color = isAimed
          ? colors.Aimed
          : isToe
            ? colors.Toe
            : colors.Edge;
        var width = BreakwaterToolSystem.EdgeWidth * this.view.ScaleAt(at);
        var middle = math.lerp(points[edge], points[(edge + 1) % points.Count], 0.5f);
        var foot = new float3(middle.x, at.y, middle.y);

        // The stem stops at the marker's rim.
        var rim = at + (math.normalizesafe(foot - at) * radius);

        buffer.DrawLine(
          color,
          color,
          0f,
          BreakwaterToolSystem.Unprojected,
          new Line3.Segment(foot, rim),
          width / 2f,
          new float2(1f, 1f)
        );

        buffer.DrawCircle(
          color,
          isToe ? color : default,
          width,
          BreakwaterToolSystem.Unprojected,
          new float2(0f, 1f),
          at,
          radius * 2f
        );
      }
    }
  }
}
