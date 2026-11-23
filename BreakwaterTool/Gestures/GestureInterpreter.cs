using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace BreakwaterTool.Gestures;

/// <summary>
/// The tool's drawing and editing rules on plain values: pointer events in, the edits they make
/// out. The tool feeds it each frame and applies what it returns through the breakwater operations.
/// It holds the outline being drawn, or the picked breakwater's as the pointer reshapes it, and
/// learns from <see cref="Edit"/> which breakwater the tool picked or created.
/// Every breakwater's vertices and edges answer the pointer as the picked one's do, the picked
/// one's first, and a gesture on one picks its breakwater, as the vanilla area tool edits any area.
/// </summary>
/// <typeparam name="TKey">What names a breakwater: its record's entity in the game.</typeparam>
/// <param name="breakwaters">
/// The outlines of the breakwaters a gesture can act on, which the owner keeps current.
/// </param>
/// <param name="heightAt">The terrain's height at a point, where the nodes lie.</param>
/// <param name="toeOf">
/// A breakwater's toe edges where they are picked by hand, null where the layout chooses them.
/// </param>
/// <param name="toeChosen">
/// The toe edges the layout chooses for an outline, one per edge, which the first edge made toe or
/// not by hand starts from.
/// </param>
internal sealed class GestureInterpreter<TKey>(
  IReadOnlyList<(TKey Breakwater, float2[] Outline)> breakwaters,
  Func<float2, float> heightAt,
  Func<TKey, IReadOnlyList<bool>?>? toeOf = null,
  Func<IReadOnlyList<float2>, bool[]>? toeChosen = null
) where TKey : struct, IEquatable<TKey> {
  private readonly List<float2> points = [];

  // Per edge of the outline, from its point i to i + 1, whether it is toe, while the picked
  // breakwater's toe is picked by hand; empty otherwise.
  private readonly List<bool> toe = [];

  // The toe edges as they were when the held vertex was grabbed.
  private bool[] toeBeforeDrag = [];

  // The outline as it was when the held vertex was grabbed: the secondary button puts it back, and
  // a release that changed nothing lays nothing.
  private float2[] beforeDrag = [];

  // Where the pointer grabbed the held vertex, and how far the vertex lay from it: the vertex keeps
  // that offset as it follows, and stays put until the pointer leaves the dead zone around the
  // grab.
  // A vertex inserted on an edge keeps none: the edge's point and the ground under the pointer lie
  // on the pointer's ray, so the vertex follows the ground under the pointer, as in the vanilla
  // area tool, and stays under it on screen.
  private float2 grabbedAt;

  private float2 grabOffset;

  private bool isInserted;

  private bool isDragging;

  internal GestureMode Mode { get; private set; }

  /// <summary>
  /// The outline being drawn, or the picked breakwater's as the pointer reshapes it, in drawing
  /// order; empty when idle.
  /// </summary>
  internal IReadOnlyList<float2> Points => this.points;

  /// <summary>
  /// The breakwater being edited, or null.
  /// </summary>
  internal TKey? Picked { get; private set; }

  /// <summary>
  /// The picked breakwater's toe edges once one was made toe or not by hand, per edge of
  /// <see cref="Points"/>. Null while the layout chooses the toe.
  /// </summary>
  internal IReadOnlyList<bool>? Toe => this.toe.Count > 0 ? this.toe : null;

  /// <summary>
  /// Whether the picked breakwater shows its toe markers: at rest, no vertex held.
  /// </summary>
  internal bool HasToeMarkers =>
    this.Mode is GestureMode.Editing && this.Held < 0 && this.points.Count >= 3;

  /// <summary>
  /// The index of the vertex the pointer holds, or -1.
  /// </summary>
  internal int Held { get; private set; } = -1;

  /// <summary>
  /// Whether the held vertex was inserted on an edge and the drag has not started: it lies on that
  /// edge, between its neighbours, and shows there.
  /// </summary>
  internal bool IsHeldOnEdge => this.Held >= 0 && this.isInserted && !this.isDragging;

  /// <summary>
  /// Whether the held vertex was inserted on an edge, dragged or not, which its discard removes.
  /// </summary>
  internal bool IsHeldInserted => this.Held >= 0 && this.isInserted;

  /// <summary>
  /// Edits a breakwater the tool picked or created, as its outline stands; called again after an
  /// edit the operations refused, it puts the outline back.
  /// </summary>
  /// <param name="toeEdges">
  /// The breakwater's toe edges where they are picked by hand, one per edge of the outline.
  /// </param>
  internal void Edit(
    TKey breakwater,
    IReadOnlyList<float2> outline,
    IReadOnlyList<bool>? toeEdges = null
  ) {
    this.Mode = GestureMode.Editing;
    this.Picked = breakwater;
    this.Held = -1;

    this.points.Clear();
    this.points.AddRange(outline);
    this.toe.Clear();

    if (toeEdges?.Count == outline.Count) {
      this.toe.AddRange(toeEdges);
    }
  }

  /// <summary>
  /// Drops the outline being drawn and the pick, as when the tool is left.
  /// </summary>
  internal void Reset() {
    this.Mode = GestureMode.Idle;
    this.Picked = null;
    this.Held = -1;

    this.points.Clear();
    this.toe.Clear();
  }

  /// <summary>
  /// What a press of the primary button would act on.
  /// </summary>
  internal GestureTarget<TKey> Aim(Pointer<TKey> pointer) {
    if (pointer.At is not {} at) {
      return new GestureTarget<TKey>(TargetKind.Nothing);
    }

    if (this.Mode is GestureMode.Drawing) {
      return this.points.Count >= 3 && math.distance(at, this.points[0]) < pointer.Reach
        ? new GestureTarget<TKey>(TargetKind.FirstPoint, 0)
        : new GestureTarget<TKey>(TargetKind.Terrain);
    }

    (TKey, IReadOnlyList<float2>)[] picked = this.Picked is {} key ? [(key, this.points)] : [];

    var others = breakwaters
      .Where(other => !this.IsPicked(other.Breakwater))
      .Select(static other => (other.Breakwater, (IReadOnlyList<float2>) other.Outline));

    var handle = this.AimHandle(picked, pointer, at) ?? this.AimHandle(others, pointer, at);

    // A node comes first, as in the vanilla area tool, then a toe marker, which floats clear of
    // its own edge and may lie over another.
    if (this.HasToeMarkers &&
      pointer.ToeMarker >= 0 &&
      pointer.ToeMarker < this.points.Count &&
      handle is not { Kind: TargetKind.Vertex }) {
      return new GestureTarget<TKey>(TargetKind.ToeMarker, pointer.ToeMarker);
    }

    if (handle is {} found) {
      return found;
    }

    return pointer.Under switch {
      {} under when under.Equals(this.Picked) => new GestureTarget<TKey>(TargetKind.Inside),
      {} under => new GestureTarget<TKey>(TargetKind.Breakwater, Breakwater: under),
      null => new GestureTarget<TKey>(TargetKind.Terrain)
    };
  }

  /// <summary>
  /// What the press of the primary button does: places a point, closes the outline, picks a
  /// breakwater, grabs a vertex, picking its breakwater, or makes a toe marker's edge toe or not.
  /// </summary>
  internal Gesture<TKey> Press(Pointer<TKey> pointer) {
    if (this.Held >= 0) {
      return new Gesture<TKey>(GestureKind.None);
    }

    var target = this.Aim(pointer);

    switch (target.Kind) {
      case TargetKind.FirstPoint: return new Gesture<TKey>(GestureKind.Closed, [.. this.points]);

      case TargetKind.Terrain when pointer.At is {} at:
        if (this.Mode is GestureMode.Drawing &&
          GestureInterpreter<TKey>.Nearest(this.points, at, pointer.Reach) >= 0) {
          return new Gesture<TKey>(GestureKind.Refused, Refusal: GestureRefusal.TooClose);
        }

        if (this.Mode is not GestureMode.Drawing) {
          this.Reset();
          this.Mode = GestureMode.Drawing;
        }

        this.points.Add(at);

        return new Gesture<TKey>(GestureKind.PointAdded);

      case TargetKind.Breakwater:
        return new Gesture<TKey>(GestureKind.Picked, Breakwater: target.Breakwater);

      case TargetKind.ToeMarker: return this.ToggleToe(target.Index);

      case TargetKind.Vertex when pointer.At is {} grabbedAt:
        var grabbing = this.Take(target);

        this.Grab(target.Index, grabbedAt, isInserted: false);

        return new Gesture<TKey>(GestureKind.Grabbed, Breakwater: grabbing);

      case TargetKind.Edge when pointer.At is {} grabbedAt && target.At is {} onEdge:
        var inserting = this.Take(target);
        var before = this.points.ToArray();

        var toeBefore = this.toe.ToArray();

        this.points.Insert(target.Index + 1, onEdge);

        // Both halves of a split edge are what the edge was.
        if (this.toe.Count > 0) {
          this.toe.Insert(target.Index + 1, this.toe[target.Index]);
        }

        this.Grab(target.Index + 1, grabbedAt, isInserted: true);
        this.beforeDrag = before;
        this.toeBeforeDrag = toeBefore;

        return new Gesture<TKey>(GestureKind.Inserted, Breakwater: inserting);

      default: return new Gesture<TKey>(GestureKind.None);
    }
  }

  /// <summary>
  /// What a pointer move does: the held vertex follows it over the terrain, once it left the dead
  /// zone of half the pointer's reach around where it grabbed the vertex.
  /// </summary>
  internal Gesture<TKey> Move(Pointer<TKey> pointer) {
    if (this.Held < 0 || pointer.At is not {} at) {
      return new Gesture<TKey>(GestureKind.None);
    }

    if (!this.isDragging && math.distance(at, this.grabbedAt) < pointer.Reach / 2f) {
      return new Gesture<TKey>(GestureKind.None);
    }

    this.isDragging = true;
    this.points[this.Held] = at + this.grabOffset;

    return new Gesture<TKey>(GestureKind.Moved);
  }

  /// <summary>
  /// What the release of the primary button does: the picked breakwater takes the outline the held
  /// vertex left, when it changed.
  /// </summary>
  internal Gesture<TKey> Release(Pointer<TKey> pointer) {
    if (this.Held < 0) {
      return new Gesture<TKey>(GestureKind.None);
    }

    this.Move(pointer);

    this.Held = -1;

    return this.points.SequenceEqual(this.beforeDrag)
      ? new Gesture<TKey>(GestureKind.Dropped)
      : new Gesture<TKey>(GestureKind.Reshaped, [.. this.points], this.Picked) {
        Toe = this.ToeNow()
      };
  }

  /// <summary>
  /// What the secondary button does, each press one step back: it cancels the drag, removes the
  /// last point drawn or the vertex under the pointer, picking its breakwater, then unpicks.
  /// With nothing drawn or picked it does nothing, as in the vanilla area tool.
  /// </summary>
  internal Gesture<TKey> Secondary(Pointer<TKey> pointer) {
    if (this.Held >= 0) {
      this.PutBack();

      return new Gesture<TKey>(GestureKind.Dropped);
    }

    switch (this.Mode) {
      case GestureMode.Drawing:
        this.points.RemoveAt(this.points.Count - 1);

        if (this.points.Count == 0) {
          this.Mode = GestureMode.Idle;
        }

        return new Gesture<TKey>(GestureKind.PointRemoved);

      case not GestureMode.Drawing when this.Aim(pointer) is { Kind: TargetKind.Vertex } vertex:
        if (this.OutlineOf(vertex).Count <= 3) {
          return new Gesture<TKey>(GestureKind.Refused, Refusal: GestureRefusal.KeepsThree);
        }

        this.Take(vertex);
        this.points.RemoveAt(vertex.Index);

        // The edge that arrived at the vertex now runs to the next one, and stays what it was.
        if (this.toe.Count > 0) {
          this.toe.RemoveAt(vertex.Index);

          if (!this.toe.Contains(true)) {
            this.toe.Clear();
          }
        }

        return new Gesture<TKey>(GestureKind.Reshaped, [.. this.points], this.Picked) {
          Toe = this.ToeNow()
        };

      case GestureMode.Editing:
        this.Reset();

        return new Gesture<TKey>(GestureKind.Unpicked);

      default: return new Gesture<TKey>(GestureKind.None);
    }
  }

  /// <summary>
  /// The hint at the pointer: the next gesture its place allows.
  /// </summary>
  internal GestureHint Hint(Pointer<TKey> pointer) => this.Hint(this.Aim(pointer));

  /// <summary>
  /// The hint at what <see cref="Aim"/> found at the pointer, for a caller that aims once a frame:
  /// aiming weighs every edge a gesture can act on.
  /// </summary>
  internal GestureHint Hint(GestureTarget<TKey> target) {
    if (this.Held >= 0) {
      return GestureHint.Release;
    }

    return (this.Mode, target.Kind) switch {
      (_, TargetKind.Nothing) => GestureHint.None,
      (_, TargetKind.FirstPoint) => GestureHint.Close,
      (GestureMode.Drawing, _) => this.points.Count < 3
        ? GestureHint.PlaceNext
        : GestureHint.PlaceOrClose,
      (_, TargetKind.Breakwater) => GestureHint.Pick,
      (_, TargetKind.Vertex) => this.OutlineOf(target).Count > 3
        ? GestureHint.MoveOrRemove
        : GestureHint.Move,
      (_, TargetKind.Edge) => GestureHint.Insert,
      (_, TargetKind.ToeMarker) => GestureHint.ToggleToe,
      (_, TargetKind.Inside) => GestureHint.Reshape,
      (GestureMode.Editing, _) => GestureHint.StartAnother,
      _ => GestureHint.PlaceFirst
    };
  }

  private bool IsPicked(TKey breakwater) => this.Picked is {} picked && picked.Equals(breakwater);

  /// <summary>
  /// The outline whose vertex or edge a target is.
  /// </summary>
  private IReadOnlyList<float2> OutlineOf(GestureTarget<TKey> target) =>
    target.Breakwater is {} breakwater && !this.IsPicked(breakwater)
      ? breakwaters.First(other => other.Breakwater.Equals(breakwater)).Outline
      : this.points;

  /// <summary>
  /// Picks the breakwater whose vertex or edge a target is, when it is not the picked one, and
  /// returns it; null when it was already picked.
  /// </summary>
  private TKey? Take(GestureTarget<TKey> target) {
    if (target.Breakwater is not {} breakwater || this.IsPicked(breakwater)) {
      return null;
    }

    this.Edit(breakwater, this.OutlineOf(target), toeOf?.Invoke(breakwater));

    return breakwater;
  }

  /// <summary>
  /// Puts the outline back as it was when the held vertex was grabbed, and lets the vertex go.
  /// </summary>
  private void PutBack() {
    this.points.Clear();
    this.points.AddRange(this.beforeDrag);

    if (this.isInserted) {
      this.toe.Clear();
      this.toe.AddRange(this.toeBeforeDrag);
    }

    this.Held = -1;
  }

  /// <summary>
  /// Makes an edge of the picked breakwater toe or not, starting from the toe the layout chose
  /// where none was picked by hand yet; refused on the last toe edge, as the rows start on the toe.
  /// </summary>
  private Gesture<TKey> ToggleToe(int edge) {
    var edges = this.toe.Count > 0 ? this.toe.ToArray() : toeChosen?.Invoke(this.points);

    if (edges?.Length != this.points.Count) {
      return new Gesture<TKey>(GestureKind.None);
    }

    if (edges[edge] && edges.Count(static isToe => isToe) == 1) {
      return new Gesture<TKey>(GestureKind.Refused, Refusal: GestureRefusal.KeepsToe);
    }

    edges[edge] = !edges[edge];
    this.toe.Clear();
    this.toe.AddRange(edges);

    return new Gesture<TKey>(GestureKind.ToeToggled, [.. this.points], this.Picked) {
      Toe = edges
    };
  }

  private bool[]? ToeNow() => this.toe.Count > 0 ? [.. this.toe] : null;

  private void Grab(int vertex, float2 at, bool isInserted) {
    this.Held = vertex;
    this.beforeDrag = [.. this.points];
    this.grabbedAt = at;
    this.grabOffset = isInserted ? float2.zero : this.points[vertex] - at;
    this.isInserted = isInserted;
    this.isDragging = false;
  }

  /// <summary>
  /// The vertex nearest the pointer within its reach among some outlines, else the edge nearest its
  /// ray, as the vanilla area tool prefers a node; null when none is within reach.
  /// </summary>
  private GestureTarget<TKey>? AimHandle(
    IEnumerable<(TKey Breakwater, IReadOnlyList<float2> Outline)> outlines,
    Pointer<TKey> pointer,
    float2 at
  ) {
    GestureTarget<TKey>? vertex = null;
    GestureTarget<TKey>? edge = null;
    var vertexReach = pointer.Reach;
    var edgeShare = 1f;

    // Each outline searches within the nearest found so far, so the last found is the nearest.
    foreach (var (breakwater, outline) in outlines) {
      if (GestureInterpreter<TKey>.Nearest(outline, at, vertexReach) is var index and >= 0) {
        vertex = new GestureTarget<TKey>(TargetKind.Vertex, index, breakwater);
        vertexReach = math.distance(at, outline[index]);
      }

      if (this.NearestEdge(outline, pointer, at, edgeShare) is var (i, onEdge, share) and
        (>= 0, _, _)) {
        edge = new GestureTarget<TKey>(TargetKind.Edge, i, breakwater, onEdge);
        edgeShare = share;
      }
    }

    return vertex ?? edge;
  }

  /// <summary>
  /// The edge of an outline nearest the pointer's ray, edge i from point i to i + 1, the point of
  /// it the ray passes nearest, and how near, as a share of the reach there; an index of -1 when
  /// none passes nearer than the share given.
  /// An edge runs straight from node to node, above the ground or under it, so it answers the ray
  /// from the eye through the pointer rather than the ground under the pointer, within the angle
  /// the reach makes at the ground: where it is drawn on screen.
  /// </summary>
  private (int Edge, float2 At, float Share) NearestEdge(
    IReadOnlyList<float2> outline,
    Pointer<TKey> pointer,
    float2 at,
    float share
  ) {
    var nearest = (Edge: -1, At: at, Share: share);
    var ground = new float3(at.x, heightAt(at), at.y);
    var toGround = math.distance(pointer.Eye, ground);

    if (toGround <= 0f) {
      return nearest;
    }

    var ray = (ground - pointer.Eye) / toGround;

    // [PERF] Each node lifted once, the end of an edge being the start of the next, in place of
    // both ends of each edge.
    float3? end = null;

    for (var i = 0; i < outline.Count; i++) {
      var a = outline[i];
      var b = outline[(i + 1) % outline.Count];
      var start = end ?? new float3(a.x, heightAt(a), a.y);

      end = new float3(b.x, heightAt(b), b.y);

      var (along, depth, distance) =
        GestureInterpreter<TKey>.RayToSegment(pointer.Eye, ray, start, end.Value);

      // The reach at the ground, scaled to the depth the ray passes the edge at.
      var reach = pointer.Reach * depth / toGround;

      if (reach > 0f && distance / reach < nearest.Share) {
        nearest = (i, math.lerp(a, b, along), distance / reach);
      }
    }

    return nearest;
  }

  /// <summary>
  /// Where a ray passes nearest a segment: how far along the segment, from 0 at its start to 1 at
  /// its end, how far along the ray, and how near (Ericson's closest points of two segments, the
  /// ray unbounded past its origin).
  /// </summary>
  /// <param name="direction">The ray's direction, of length 1.</param>
  private static (float Along, float Depth, float Distance) RayToSegment(
    float3 origin,
    float3 direction,
    float3 start,
    float3 end
  ) {
    var span = end - start;
    var offset = origin - start;
    var lengthSq = math.lengthsq(span);
    var c = math.dot(direction, offset);

    if (lengthSq <= 0f) {
      var at = math.max(0f, -c);

      return (0f, at, math.distance(origin + (at * direction), start));
    }

    var b = math.dot(direction, span);
    var f = math.dot(span, offset);
    var denominator = lengthSq - (b * b);
    var depth = denominator > 0f ? math.max(0f, ((b * f) - (c * lengthSq)) / denominator) : 0f;
    var along = ((b * depth) + f) / lengthSq;

    if (along < 0f) {
      along = 0f;
      depth = math.max(0f, -c);
    }
    else if (along > 1f) {
      along = 1f;
      depth = math.max(0f, b - c);
    }

    var distance = math.distance(origin + (depth * direction), start + (along * span));

    return (along, depth, distance);
  }

  /// <summary>
  /// The index of the point nearest the pointer within its reach, or -1.
  /// </summary>
  private static int Nearest(IReadOnlyList<float2> candidates, float2 at, float reach) {
    var nearest = -1;
    var best = reach;

    for (var i = 0; i < candidates.Count; i++) {
      var distance = math.distance(at, candidates[i]);

      if (distance < best) {
        nearest = i;
        best = distance;
      }
    }

    return nearest;
  }
}

internal enum GestureMode {
  /// <summary>
  /// Nothing drawn, nothing picked.
  /// </summary>
  Idle,

  /// <summary>
  /// An outline is being drawn, not closed yet.
  /// </summary>
  Drawing,

  /// <summary>
  /// A breakwater is picked, its outline editable.
  /// </summary>
  Editing
}

/// <summary>
/// Where the pointer is, for one event.
/// </summary>
/// <param name="At">
/// Where the pointer meets the terrain, under water too, seen from above, or null where it meets
/// none.
/// </param>
/// <param name="Reach">
/// How far from a node or an edge the pointer still holds it, in metres: vanilla's snap distance
/// for a surface area, never less than 12 pixels on screen.
/// </param>
/// <param name="Under">The breakwater whose outline holds the pointer, or null.</param>
/// <param name="Eye">
/// Where the pointer's ray starts, the camera: an edge answers the ray, a node the ground.
/// </param>
/// <param name="ToeMarker">
/// The edge of the picked breakwater whose toe marker the pointer's ray is on, or -1.
/// </param>
internal readonly record struct Pointer<TKey>(
  float2? At,
  float Reach,
  TKey? Under,
  float3 Eye,
  int ToeMarker = -1
) where TKey : struct;

internal enum TargetKind {
  /// <summary>
  /// The pointer meets no ground.
  /// </summary>
  Nothing,

  /// <summary>
  /// Open ground, where a press places a point.
  /// </summary>
  Terrain,

  /// <summary>
  /// The first point of an outline of three points or more, which a press closes.
  /// </summary>
  FirstPoint,

  /// <summary>
  /// A vertex of a breakwater, the picked one's before any other's.
  /// </summary>
  Vertex,

  /// <summary>
  /// A point of an edge of a breakwater, the picked one's before any other's, where a press inserts
  /// a vertex.
  /// </summary>
  Edge,

  /// <summary>
  /// The toe marker of an edge of the picked breakwater, where a press makes the edge toe or not.
  /// </summary>
  ToeMarker,

  /// <summary>
  /// The picked breakwater's area, away from its handles.
  /// </summary>
  Inside,

  /// <summary>
  /// The area of a breakwater not picked, which a press picks.
  /// </summary>
  Breakwater
}

/// <param name="Index">
/// The vertex, or the edge, from point i to i + 1, a toe marker's edge included.
/// </param>
/// <param name="Breakwater">The breakwater a press picks, or whose vertex or edge it is.</param>
/// <param name="At">The point of the edge a press inserts a vertex at.</param>
internal readonly record struct GestureTarget<TKey>(
  TargetKind Kind,
  int Index = -1,
  TKey? Breakwater = null,
  float2? At = null
) where TKey : struct;

internal enum GestureKind {
  /// <summary>
  /// Nothing changes.
  /// </summary>
  None,

  /// <summary>
  /// A point is placed; the first one leaves the breakwater that was picked.
  /// </summary>
  PointAdded,

  /// <summary>
  /// The last point drawn is removed.
  /// </summary>
  PointRemoved,

  /// <summary>
  /// The outline closes on its first point: a breakwater is to be created on the polygon.
  /// </summary>
  Closed,

  /// <summary>
  /// A breakwater is to be picked: the tool reads it and calls <c>Edit</c>.
  /// </summary>
  Picked,

  /// <summary>
  /// The pick is left.
  /// </summary>
  Unpicked,

  /// <summary>
  /// A vertex is held; the breakwater named, when there is one, is the one the press picked.
  /// </summary>
  Grabbed,

  /// <summary>
  /// A vertex is inserted on an edge, and held; the breakwater named, when there is one, is the one
  /// the press picked.
  /// </summary>
  Inserted,

  /// <summary>
  /// The held vertex follows the pointer: the outline changes, and nothing is laid yet.
  /// </summary>
  Moved,

  /// <summary>
  /// The picked breakwater is to be laid on the polygon.
  /// </summary>
  Reshaped,

  /// <summary>
  /// The held vertex is let go with the outline as it was: nothing is laid.
  /// </summary>
  Dropped,

  /// <summary>
  /// A click made an edge toe or not: the gesture carries the outline and its toe edges.
  /// </summary>
  ToeToggled,

  /// <summary>
  /// The gesture does nothing, for a reason the player is told.
  /// </summary>
  Refused
}

internal enum GestureRefusal {
  None,

  /// <summary>
  /// A point would land on another point of the outline.
  /// </summary>
  TooClose,

  /// <summary>
  /// A breakwater keeps three vertices at least.
  /// </summary>
  KeepsThree,

  /// <summary>
  /// The edge is the last of the toe, where the rows start.
  /// </summary>
  KeepsToe
}

/// <param name="Polygon">The outline to create or lay, for a close or a reshape.</param>
/// <param name="Breakwater">
/// The breakwater to pick or to lay again, or the one a grab picked.
/// </param>
internal readonly record struct Gesture<TKey>(
  GestureKind Kind,
  float2[]? Polygon = null,
  TKey? Breakwater = null,
  GestureRefusal Refusal = GestureRefusal.None
) where TKey : struct {
  /// <summary>
  /// The outline's toe edges after the gesture, where they are picked by hand; null where the
  /// layout chooses them.
  /// </summary>
  internal bool[]? Toe { get; init; }
}

/// <summary>
/// The hint at the pointer, each the gestures its place allows.
/// </summary>
internal enum GestureHint {
  None,

  PlaceFirst,

  PlaceNext,

  PlaceOrClose,

  Close,

  Pick,

  Move,

  MoveOrRemove,

  Insert,

  Reshape,

  StartAnother,

  ToggleToe,

  Release
}
