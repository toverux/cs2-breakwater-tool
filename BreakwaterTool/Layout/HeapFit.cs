using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace BreakwaterTool.Layout;

/// <summary>
/// Fits dumped stones to the ground and to each other, whatever the prop, so a heap looks placed:
/// each stone in turn, in the order the rows laid them, starts from the resting pose nearest the
/// one the rows gave it, drops onto the heap at the lowest of a few spots round its place, then
/// turns and slides by ever smaller steps while that lowers its centre. A body at rest under its
/// weight sits where no small move lowers it, so this asks nothing of a prop but its mesh.
/// The heap is a grid of the highest point under each cell, the ground's or a stone's: a stone
/// rests on what is under it, never inside it, and nestles between the legs or flanks of others.
/// </summary>
internal static class HeapFit {
  /// <summary>
  /// The spots round a stone's place it is first dropped at, besides the place itself.
  /// </summary>
  private const int Pockets = 12;

  /// <summary>
  /// How far round its place a stone looks for a lower spot, in its nominal sizes.
  /// </summary>
  private const float PocketReach = 0.5f;

  /// <summary>
  /// What moving a stone off its place costs, in height, per square of nominal sizes moved: it
  /// keeps a stone near its place, so the heap stays as even as the rows.
  /// </summary>
  private const float Spring = 1f;

  /// <summary>
  /// The first and the last turn of the settling, in degrees, and the first slide in nominal sizes.
  /// </summary>
  private const float FirstTurn = 12f;

  private const float LastTurn = 1.5f;

  private const float FirstSlide = 0.06f;

  /// <summary>
  /// How deep a stone may sit into those under it, in its nominal size: meshes are rounder than the
  /// points they are tested on, and a stone resting on another touches it.
  /// </summary>
  private const float Contact = 0.03f;

  /// <summary>
  /// The heap grid's step, in nominal sizes.
  /// </summary>
  private const float CellShare = 1f / 8f;

  /// <summary>
  /// The most cells the heap grid holds; a coarser step keeps a vast outline under it.
  /// </summary>
  private const int Cap = 4_000_000;

  /// <param name="stones">
  /// The stones as the rows laid them, each turned from its prefab's frame: their order is the
  /// order they are dumped in.
  /// </param>
  /// <param name="shapes">Each stone shape's fitting data, as <see cref="LaidStone.Shape"/>
  /// indexes them.</param>
  /// <param name="embed">How deep the stones sit into the ground, in their rested height.</param>
  internal static List<LaidStone> Fit(
    IReadOnlyList<LaidStone> stones,
    IReadOnlyList<HeapShape> shapes,
    ILayoutHost host,
    float embed,
    uint seed
  ) {
    var fitted = new List<LaidStone>(stones.Count);

    if (stones.Count == 0) {
      return fitted;
    }

    var random = new Random(math.max(1u, (seed * 4253u) + 17u));
    var grid = HeapFit.Grid(stones, shapes, host);

    foreach (var stone in stones) {
      var shape = shapes[stone.Shape];
      var sink = embed * shape.Rested.Height;
      var contact = HeapFit.Contact * shape.Nominal;
      var place = stone.Position.xz + math.rotate(stone.Rotation, shape.Centroid).xz;
      var rotation = HeapFit.NearestPose(shape, stone.Rotation);

      // The height of the stone's centre once dropped turned and centred so, and the pivot it then
      // stands on, with what moving it off its place costs.
      float Height(quaternion turned, float2 over, out float3 foot) {
        var offset = math.rotate(turned, shape.Centroid);
        var under = over - offset.xz;
        var y = float.MinValue;

        foreach (var point in shape.Probe) {
          var probe = math.rotate(turned, point);

          y = math.max(y, grid.Top(under + probe.xz, sink) - probe.y);
        }

        foot = new float3(under.x, y - contact, under.y);

        return foot.y + offset.y + (HeapFit.Spring * math.lengthsq(over - place) / shape.Nominal);
      }

      var centre = place;
      var best = Height(rotation, centre, out var pivot);

      for (var k = 0; k < HeapFit.Pockets; k++) {
        var angle = (2f * math.PI * k / HeapFit.Pockets) + random.NextFloat(0.5f);
        var spot = place +
          (HeapFit.PocketReach * shape.Nominal * new float2(math.cos(angle), math.sin(angle)));
        var turn = math.mul(quaternion.RotateY(random.NextFloat(2f * math.PI)), rotation);
        var height = Height(turn, spot, out var at);

        if (height < best) {
          best = height;
          centre = spot;
          pivot = at;
          rotation = turn;
        }
      }

      var angleStep = math.radians(HeapFit.FirstTurn);
      var slide = HeapFit.FirstSlide * shape.Nominal;

      while (angleStep >= math.radians(HeapFit.LastTurn)) {
        var moved = false;
        var nextTurn = rotation;
        var nextCentre = centre;
        var nextPivot = pivot;

        for (var move = 0; move < 10; move++) {
          var turn = rotation;
          var spot = centre;

          if (move < 6) {
            var axis = (move / 2) switch {
              0 => new float3(1f, 0f, 0f),
              1 => new float3(0f, 0f, 1f),
              _ => new float3(0f, 1f, 0f)
            };

            var sign = move % 2 == 0 ? 1f : -1f;

            turn = math.normalize(math.mul(quaternion.AxisAngle(axis, sign * angleStep), rotation));
          } else {
            var sign = move % 2 == 0 ? 1f : -1f;

            spot += move < 8 ? new float2(sign * slide, 0f) : new float2(0f, sign * slide);
          }

          var height = Height(turn, spot, out var at);

          if (height < best - (1e-5f * shape.Nominal)) {
            best = height;
            nextTurn = turn;
            nextCentre = spot;
            nextPivot = at;
            moved = true;
          }
        }

        if (moved) {
          rotation = nextTurn;
          centre = nextCentre;
          pivot = nextPivot;
        } else {
          angleStep *= 0.5f;
          slide *= 0.5f;
        }
      }

      foreach (var point in shape.Surface) {
        grid.Raise(pivot + math.rotate(rotation, point));
      }

      fitted.Add(stone with {
        Position = pivot,
        Rotation = rotation
      });
    }

    return fitted;
  }

  /// <summary>
  /// The resting pose whose down is nearest the one a rotation turns down, turned as little as it
  /// takes from that rotation, so the rows' heading and the tumble's draw carry over.
  /// </summary>
  internal static quaternion NearestPose(HeapShape shape, quaternion rotation) {
    var down = HeapShape.Down(rotation);
    var nearest = shape.Poses[0];
    var closest = float.MinValue;

    foreach (var pose in shape.Poses) {
      var dot = math.dot(HeapShape.Down(pose), down);

      if (dot > closest) {
        closest = dot;
        nearest = pose;
      }
    }

    return math.normalize(math.mul(rotation, HeapFit.FromTo(HeapShape.Down(nearest), down)));
  }

  /// <summary>
  /// The shortest rotation from one unit vector to another.
  /// </summary>
  private static quaternion FromTo(float3 from, float3 to) {
    var axis = math.cross(from, to);
    var cos = math.clamp(math.dot(from, to), -1f, 1f);

    if (math.lengthsq(axis) < 1e-10f) {
      if (cos > 0f) {
        return quaternion.identity;
      }

      var other = math.abs(from.x) < 0.9f ? new float3(1f, 0f, 0f) : new float3(0f, 1f, 0f);

      return quaternion.AxisAngle(math.normalize(math.cross(from, other)), math.PI);
    }

    return quaternion.AxisAngle(math.normalize(axis), math.acos(cos));
  }

  private static HeapGrid Grid(
    IReadOnlyList<LaidStone> stones,
    IReadOnlyList<HeapShape> shapes,
    ILayoutHost host
  ) {
    var used = stones
      .Select(static stone => stone.Shape)
      .Distinct()
      .Select(index => shapes[index])
      .ToList();
    var reach = 2f * used.Max(static shape => shape.Size);
    var min = new float2(float.MaxValue);
    var max = new float2(float.MinValue);

    foreach (var stone in stones) {
      min = math.min(min, stone.Position.xz);
      max = math.max(max, stone.Position.xz);
    }

    min -= reach;
    max += reach;

    var span = max - min;
    var cell = HeapFit.CellShare * used.Average(static shape => shape.Nominal);

    cell = math.max(cell, math.sqrt(span.x * span.y / HeapFit.Cap) * 1.01f);

    return new HeapGrid(min, span, cell, host);
  }

  /// <summary>
  /// The highest point of the stones laid so far over each cell of a grid, and the ground under it.
  /// </summary>
  private sealed class HeapGrid {
    private readonly float2 origin;

    private readonly float cell;

    private readonly int countX;

    private readonly int countZ;

    private readonly float[] tops;

    private readonly ILayoutHost host;

    internal HeapGrid(float2 origin, float2 span, float cell, ILayoutHost host) {
      this.origin = origin;
      this.cell = cell;
      this.countX = (int) math.ceil(span.x / cell) + 1;
      this.countZ = (int) math.ceil(span.y / cell) + 1;
      this.tops = Enumerable.Repeat(float.MinValue, this.countX * this.countZ).ToArray();
      this.host = host;
    }

    /// <summary>
    /// What a stone rests on at a point: the highest stone there, or the ground less how deep the
    /// stone may sit into it.
    /// </summary>
    internal float Top(float2 xz, float sink) {
      var ground = this.host.HeightAt(xz) - sink;
      var index = this.Index(xz);

      return index < 0 ? ground : math.max(ground, this.tops[index]);
    }

    internal void Raise(float3 point) {
      var index = this.Index(point.xz);

      if (index >= 0) {
        this.tops[index] = math.max(this.tops[index], point.y);
      }
    }

    private int Index(float2 xz) {
      var at = (int2) math.floor((xz - this.origin) / this.cell);

      return math.any(at < 0) || at.x >= this.countX || at.y >= this.countZ
        ? -1
        : at.x + (at.y * this.countX);
    }
  }
}
