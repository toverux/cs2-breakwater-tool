using System.Collections.Generic;
using System.Linq;
using BreakwaterTool.Gestures;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Game.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace BreakwaterTool.Breakwaters {
  /// <summary>
  /// The overlay, in the vanilla area tool's style: every breakwater's outline, the one drawn or
  /// picked with its nodes and its toe, the edges the layout climbs from, and the angles at the
  /// point placed or the vertex dragged.
  /// Sizes are vanilla's for a surface area, in metres, with floors on screen so that a far outline
  /// stays visible and within reach.
  /// It is drawn as vanilla draws a surface area: the nodes on the terrain, under water too, where
  /// the pointer meets it, and the edges straight from node to node, unprojected, as a projected
  /// line paints everything under it, the stones and the faces of a cliff included.
  /// </summary>
  internal sealed partial class BreakwaterToolSystem {
    // Vanilla's snap distance for a surface area, the area closest to a breakwater's outline, in
    // metres: how far a node or an edge reaches, and the scale of the lines and nodes.
    private const float SnapDistance = 0.75f;

    // Vanilla's edge width and node diameters, in snap distances.
    private const float EdgeWidth = 0.3f;

    private const float NodeSize = 0.3f;

    private const float ActiveNodeSize = 0.5f;

    // Vanilla's style for a surface area's overlay: no flag, drawn where it is, seen through the
    // ground and the stones in front of it.
    private const OverlayRenderSystem.StyleFlags Unprojected = 0;

    // Vanilla caps the size of its angle readout at this snap distance.
    private const float AngleSnapLimit = 16f;

    // The floors on screen, in pixels of a screen 1080 high, under which vanilla's sizes shrink
    // from afar: an edge never thinner than WCAG's minimum focus indicator, 2 pixels, and a reach
    // never under half of WCAG's minimum target, 24 pixels across.
    private const float EdgePixels = 2f;

    private const float ReachPixels = 12f;

    private static readonly Color NodeColor = new(1f, 1f, 1f, 0.5f);

    // The outline of a breakwater not picked: a dim white, set apart from the picked one's colours,
    // and brighter under the pointer.
    private static readonly Color OtherColor = new(1f, 1f, 1f, 0.4f);

    private static readonly Color HoveredOtherColor = new(1f, 1f, 1f, 0.7f);

    private readonly List<(float3 At, int Degrees)> angles = [];

    private OverlayRenderSystem overlayRenderSystem = null!;

    // The camera this frame, read before the pointer and the overlay.
    private View view;

    private EntityQuery renderingSettingsQuery;

    private EntityQuery guideLineSettingsQuery;

    /// <summary>
    /// The angles the overlay shows this frame, where each one's value shows and the value in
    /// degrees, for the tooltip system.
    /// </summary>
    internal IReadOnlyList<(float3 At, int Degrees)> Angles => this.angles;

    private void OnCreateOverlay() {
      this.overlayRenderSystem = this.World.GetOrCreateSystemManaged<OverlayRenderSystem>();

      this.renderingSettingsQuery =
        this.GetEntityQuery(ComponentType.ReadOnly<RenderingSettingsData>());

      this.guideLineSettingsQuery =
        this.GetEntityQuery(ComponentType.ReadOnly<GuideLineSettingsData>());
    }

    /// <param name="target">What a press would act on, as the gestures aim this frame.</param>
    private void DrawOverlay(GestureTarget<Entity> target) {
      this.angles.Clear();

      var hasRendering =
        this.renderingSettingsQuery.TryGetSingleton<RenderingSettingsData>(out var rendering);

      var edgeColor = hasRendering ? rendering.m_HoveredColor : new Color(0.5f, 0.5f, 1f, 0.5f);

      var guides = this.guideLineSettingsQuery.TryGetSingleton<GuideLineSettingsData>(out var found)
        ? found
        : default;

      var toeColor = BreakwaterToolSystem.Opaque(guides.m_PositiveFeedbackColor, edgeColor.a);
      var highlight = guides.m_HighPriorityColor;

      var buffer = this.overlayRenderSystem.GetBuffer(out var deps);

      deps.Complete();

      var held = this.gestures.Held;
      var isHolding = held >= 0;

      // The other breakwaters in view, the aimed one and the one under the pointer brighter.
      this.DrawOthers(
        buffer,
        isHolding ? null : target.Breakwater,
        isHolding ? null : this.pointer.Under
      );

      // The node or the edge point a press would take on one of them.
      if (!isHolding &&
        target.Breakwater is {} targeted &&
        targeted != this.gestures.Picked &&
        this.inView.Find(outline => outline.Record == targeted).Outline is {} polygon) {
        if (target.Kind is TargetKind.Vertex) {
          Node(highlight, Lift(polygon[target.Index]), BreakwaterToolSystem.ActiveNodeSize);
        }

        if (target is { Kind: TargetKind.Edge, At: {} onOther }) {
          EdgePoint(polygon, onOther);
        }
      }

      var points = this.gestures.Points;

      if (points.Count == 0) {
        return;
      }

      var isEditing = this.gestures.Mode is GestureMode.Editing;
      var isToeKnown = this.toe.Length == points.Count;

      for (var i = 0; i < (isEditing ? points.Count : points.Count - 1); i++) {
        var color = isToeKnown && this.toe[i] ? toeColor : edgeColor;

        Edge(color, Placed(i), Placed((i + 1) % points.Count));
      }

      if (isEditing) {
        DrawEditing();
      }
      else {
        DrawDrawing();
      }

      // The outline being drawn, the edge to come, and the angle the pointer makes at the last
      // point.
      void DrawDrawing() {
        var isClosing = target.Kind is TargetKind.FirstPoint;

        if (this.pointer.At is {} at) {
          // The edge that would close the outline ends on the first point, as in the area tool.
          var end = isClosing ? points[0] : at;

          Edge(edgeColor, Lift(points[^1]), Lift(end));

          if (points.Count >= 2) {
            var corner = new Corner(Lift(points[^1]), Lift(end), Lift(points[^2]));

            this.DrawAngle(buffer, highlight, corner);
          }
        }

        for (var i = 1; i < points.Count; i++) {
          Node(BreakwaterToolSystem.NodeColor, Lift(points[i]), BreakwaterToolSystem.NodeSize);
        }

        Node(
          isClosing ? highlight : BreakwaterToolSystem.NodeColor,
          Lift(points[0]),
          isClosing ? BreakwaterToolSystem.ActiveNodeSize : BreakwaterToolSystem.NodeSize
        );

        if (this.pointer.At is {} pointerAt && !isClosing) {
          Node(highlight, Lift(pointerAt), BreakwaterToolSystem.ActiveNodeSize);
        }
      }

      // The picked outline, the node or the edge point aimed at, and while a vertex is dragged the
      // angles it makes at its two neighbours.
      void DrawEditing() {
        var isOwnTarget = held < 0 && target.Breakwater == this.gestures.Picked;

        for (var i = 0; i < points.Count; i++) {
          var isAimed = held == i ||
            (isOwnTarget && target is { Kind: TargetKind.Vertex } && target.Index == i);

          Node(
            isAimed ? highlight : BreakwaterToolSystem.NodeColor,
            Placed(i),
            isAimed ? BreakwaterToolSystem.ActiveNodeSize : BreakwaterToolSystem.NodeSize
          );
        }

        if (isOwnTarget && target is { Kind: TargetKind.Edge, At: {} onEdge }) {
          EdgePoint(points, onEdge);
        }

        if (held < 0) {
          this.DrawToeMarkers(buffer, target, (toeColor, edgeColor, highlight));

          return;
        }

        var count = points.Count;
        var moving = Placed(held);

        for (var side = -1; side <= 1; side += 2) {
          var vertex = points[(held + side + count) % count];
          var other = points[(held + (2 * side) + (2 * count)) % count];

          this.DrawAngle(buffer, highlight, new Corner(Lift(vertex), moving, Lift(other)));
        }
      }

      float3 Lift(float2 xz) => new(xz.x, this.HeightAt(xz), xz.y);

      // Where a node of the outline shows: on the ground, but for a vertex just inserted, which
      // shows on its edge, under the pointer, until the drag starts.
      float3 Placed(int index) {
        var count = points.Count;

        return index == held && this.gestures.IsHeldOnEdge
          ? OnEdge(points[(index + count - 1) % count], points[(index + 1) % count], points[index])
          : Lift(points[index]);
      }

      // The edge point aimed at on an outline, which a press would take.
      void EdgePoint(IReadOnlyList<float2> outline, float2 at) =>
        Node(
          highlight,
          OnEdge(outline[target.Index], outline[(target.Index + 1) % outline.Count], at),
          BreakwaterToolSystem.ActiveNodeSize
        );

      // The point of the edge from node a to node b over a point of the ground, the edge running
      // straight between its two nodes.
      float3 OnEdge(float2 a, float2 b, float2 xz) {
        var start = Lift(a);
        var end = Lift(b);
        var length = math.distance(a, b);
        var along = length > 0f ? math.saturate(math.distance(a, xz) / length) : 0f;

        return new float3(xz.x, math.lerp(start.y, end.y, along), xz.y);
      }

      // An edge straight from node to node.
      void Edge(Color color, float3 a, float3 b) {
        var edge = new Line3.Segment(a, b);
        var width = BreakwaterToolSystem.EdgeWidth *
          this.view.ScaleAt(MathUtils.Position(edge, 0.5f));

        buffer.DrawLine(
          color,
          color,
          0f,
          BreakwaterToolSystem.Unprojected,
          edge,
          width,
          new float2(1f, 1f)
        );
      }

      void Node(Color color, float3 at, float scale) {
        buffer.DrawCircle(
          color,
          color,
          0f,
          BreakwaterToolSystem.Unprojected,
          new float2(0f, 1f),
          at,
          scale * this.view.ScaleAt(at)
        );
      }
    }

    /// <summary>
    /// Draws the outline of every breakwater in view but the picked one.
    /// </summary>
    /// <param name="aimed">The breakwater a press would act on, drawn brighter.</param>
    /// <param name="hovered">The breakwater under the pointer, drawn brighter.</param>
    private void DrawOthers(OverlayRenderSystem.Buffer buffer, Entity? aimed, Entity? hovered) {
      var count = this.inView.Sum(static outline => outline.Outline.Length);

      if (count == 0) {
        return;
      }

      var points = new NativeArray<float2>(count, Allocator.TempJob);
      var ends = new NativeArray<int>(this.inView.Count, Allocator.TempJob);
      var colors = new NativeArray<Color>(this.inView.Count, Allocator.TempJob);
      var next = 0;

      for (var i = 0; i < this.inView.Count; i++) {
        var (record, outline, _) = this.inView[i];

        // The picked breakwater's outline is drawn apart, with its nodes and its toe.
        if (record != this.gestures.Picked) {
          NativeArray<float2>.Copy(outline, 0, points, next, outline.Length);
          next += outline.Length;
        }

        ends[i] = next;
        colors[i] = record == aimed || record == hovered
          ? BreakwaterToolSystem.HoveredOtherColor
          : BreakwaterToolSystem.OtherColor;
      }

      new OutlineOverlayJob {
        points = points,
        ends = ends,
        colors = colors,
        heights = this.heights,
        buffer = buffer,
        eye = this.view.Position,
        width = BreakwaterToolSystem.EdgeWidth * BreakwaterToolSystem.SnapDistance,
        widthPerMetre = BreakwaterToolSystem.EdgePixels * this.view.PixelSpan
      }.Run();

      points.Dispose();
      ends.Dispose();
      colors.Dispose();
    }

    /// <summary>
    /// Draws the angle at a corner as the vanilla area tool does, and keeps where its value shows:
    /// the two arms, then an arc, a square at a right angle, or a bracket at a flat one.
    /// </summary>
    private void DrawAngle(OverlayRenderSystem.Buffer buffer, Color color, Corner corner) {
      var vertex = corner.Vertex;
      var toward = corner.Toward.xz - vertex.xz;
      var length = math.length(toward);
      var otherDir = math.normalizesafe(corner.Other.xz - vertex.xz);

      if (length <= 0f || otherDir.Equals(default)) {
        return;
      }

      var towardDir = toward / length;
      var cosine = math.clamp(math.dot(towardDir, otherDir), -1f, 1f);
      var degrees = Mathf.RoundToInt(math.degrees(math.acos(cosine)));

      if (degrees == 0) {
        return;
      }

      var scale = math.min(
        this.view.ScaleAt(vertex),
        BreakwaterToolSystem.AngleSnapLimit
      );
      var width = scale * 0.125f;
      var arm = math.min(length, scale * 4f);
      var size = arm / 2f;

      // Vanilla's names: the two arms' directions pointing at the vertex, and the side of the arm
      // toward the moving point the angle opens to.
      var dir1 = -otherDir;
      var dir2 = -towardDir;
      var isRight = math.dot(MathUtils.Right(towardDir), otherDir) > 0f;

      Line(towardDir * arm, float2.zero);
      Line(otherDir * arm, float2.zero);

      switch (degrees) {
        case 180: {
          var side1 = Side(dir1);
          var side2 = Side(dir2);

          Line(-dir1 * size, (side1 * (size - (width / 2f))) - (dir1 * size));
          Line(
            (side1 * size) - (dir1 * (size + (width / 2f))),
            (-side2 * size) - (dir2 * (size + (width / 2f)))
          );
          Line((-side2 * (size - (width / 2f))) - (dir2 * size), -dir2 * size);
          this.angles.Add((At(side1 * (size * 1.5f)), degrees));

          break;
        }

        case > 90: {
          var middle = math.normalize(dir1 + dir2);

          Arc(-dir1 * size, Side(dir1), -middle * size, Side(middle));
          Arc(-middle * size, Side(middle), -dir2 * size, Side(dir2));
          this.angles.Add((At(-middle * (size * 1.5f)), degrees));

          break;
        }

        case 90:
          Line(-dir1 * size, (-dir2 * (size - (width / 2f))) - (dir1 * size));
          Line((-dir2 * size) - (dir1 * (size + (width / 2f))), -dir2 * size);
          this.angles.Add((At(-math.normalizesafe(dir1 + dir2) * (size * 1.5f)), degrees));

          break;

        default:
          Arc(-dir1 * size, Side(dir1), -dir2 * size, Side(dir2));
          this.angles.Add((At(-math.normalizesafe(dir1 + dir2) * (size * 1.5f)), degrees));

          break;
      }

      float3 At(float2 offset) => new(vertex.x + offset.x, vertex.y, vertex.z + offset.y);

      float2 Side(float2 direction) =>
        isRight ? MathUtils.Right(direction) : MathUtils.Left(direction);

      void Line(float2 from, float2 to) =>
        buffer.DrawLine(
          color,
          color,
          0f,
          BreakwaterToolSystem.Unprojected,
          new Line3.Segment(At(from), At(to)),
          width,
          default
        );

      void Arc(float2 from, float2 startTangent, float2 to, float2 endTangent) =>
        buffer.DrawCurve(
          color,
          color,
          0f,
          BreakwaterToolSystem.Unprojected,
          NetUtils.FitCurve(
            At(from),
            new float3(startTangent.x, 0f, startTangent.y),
            new float3(endTangent.x, 0f, endTangent.y),
            At(to)
          ),
          width
        );
    }

    private static Color Opaque(Color color, float alpha) => new(color.r, color.g, color.b, alpha);

    /// <summary>
    /// Where the camera is and how wide it sees, read once a frame: the overlay scales each of its
    /// many pieces from it.
    /// </summary>
    /// <param name="PixelSpan">
    /// The metres a pixel of a screen 1080 high spans per metre away from the camera; 0 without a
    /// camera.
    /// </param>
    private readonly record struct View(float3 Position, float PixelSpan) {
      internal static View Of(Camera? camera) {
        // Unity's == also sees a destroyed camera, which `is null` misses.
        if (camera == null) {
          return default;
        }

        var span = 2f * math.tan(math.radians(camera.fieldOfView) / 2f) / 1080f;

        return new View(camera.transform.position, span);
      }

      /// <summary>
      /// The snap distance that scales lines and nodes at a point, in metres: vanilla's, or more
      /// where the camera is so far that an edge would show thinner than
      /// <see cref="BreakwaterToolSystem.EdgePixels"/>.
      /// </summary>
      internal float ScaleAt(float3 at) =>
        math.max(
          BreakwaterToolSystem.SnapDistance,
          BreakwaterToolSystem.EdgePixels / BreakwaterToolSystem.EdgeWidth * this.MetresPerPixel(at)
        );

      /// <summary>
      /// How far a node or an edge reaches at a point, in metres: vanilla's snap distance, or more
      /// where the camera is so far that it would show shorter than
      /// <see cref="BreakwaterToolSystem.ReachPixels"/>.
      /// </summary>
      internal float ReachAt(float3 at) =>
        math.max(
          BreakwaterToolSystem.SnapDistance,
          BreakwaterToolSystem.ReachPixels * this.MetresPerPixel(at)
        );

      private float MetresPerPixel(float3 at) => math.distance(this.Position, at) * this.PixelSpan;
    }

    /// <summary>
    /// A corner whose angle shows: the vertex, the point its arm toward moves, and the other end.
    /// </summary>
    private readonly record struct Corner(float3 Vertex, float3 Toward, float3 Other);
  }
}
