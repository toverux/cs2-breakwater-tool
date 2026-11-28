// SPIKE (ticket 04), throwaway: fitting approaches over a heap height grid.
using System;
using System.Collections.Generic;
using System.Linq;
using BreakwaterTool.Layout;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace BreakwaterTool.Tests.FitSpike;

/// <summary>A unit's geometry for fitting: surface points in the prefab frame.</summary>
internal sealed class FitShape {
  public required float3[] Fine { get; init; }
  public required float3[] Probe { get; init; }
  public required float3 Centroid { get; init; }

  /// <summary>The longest side of the bounds.</summary>
  public required float Size { get; init; }

  /// <summary>The nominal size, the cube root of the volume: what the grid and the tolerances scale by.</summary>
  public required float Nominal { get; init; }

  public quaternion[] Poses { get; set; } = [];

  public float Fill { get; set; } = 1f;

  /// <summary>The pose most starts come to rest in.</summary>
  public quaternion Rest { get; set; } = quaternion.identity;

  public static FitShape From(SpikeMesh mesh) {
    var lo = mesh.Vertices.Aggregate(new float3(float.MaxValue), math.min);
    var hi = mesh.Vertices.Aggregate(new float3(float.MinValue), math.max);
    var size = math.cmax(hi - lo);
    var voxel = size / 32f;
    var volume = HeapMetrics.Solid(mesh, voxel).Length * voxel * voxel * voxel;
    var nominal = math.max(math.pow(volume, 1f / 3f), 0.05f * size);
    var areaSum = 0f;
    var centroid = float3.zero;

    foreach (var t in mesh.Triangles) {
      var a = mesh.Vertices[t.x];
      var b = mesh.Vertices[t.y];
      var c = mesh.Vertices[t.z];
      var area = 0.5f * math.length(math.cross(b - a, c - a));
      areaSum += area;
      centroid += area * (a + b + c) / 3f;
    }

    var fine = Sample(mesh, nominal / 10f);
    var shape = new FitShape {
      Fine = fine,
      Probe = Farthest(Sample(mesh, nominal / 6f), (int) math.clamp(32f + (16f * size / nominal), 48f, 160f)),
      Centroid = areaSum > 0f ? centroid / areaSum : (lo + hi) / 2f,
      Size = size,
      Nominal = nominal
    };

    shape.Poses = SettleFit.StablePoses(shape);
    shape.Rest = shape.Poses
      .GroupBy(p => (int3) math.round(math.rotate(math.inverse(p), new float3(0, -1, 0)) * 4f))
      .OrderByDescending(g => g.Count())
      .First()
      .First();

    return shape;
  }

  /// <summary>A subset spread as far apart as it goes, the first point the lowest.</summary>
  public static float3[] Farthest(float3[] points, int count) {
    if (points.Length <= count) {
      return points;
    }

    var chosen = new List<float3>();
    var d = Enumerable.Repeat(float.MaxValue, points.Length).ToArray();
    var next = 0;

    for (var k = 0; k < count; k++) {
      chosen.Add(points[next]);
      var far = -1f;

      for (var i = 0; i < points.Length; i++) {
        d[i] = math.min(d[i], math.distancesq(points[i], points[next]));
      }

      for (var i = 0; i < points.Length; i++) {
        if (d[i] > far) {
          far = d[i];
          next = i;
        }
      }
    }

    return [.. chosen];
  }

  /// <summary>Points on the surface at about a spacing, one per cell of that size.</summary>
  public static float3[] Sample(SpikeMesh mesh, float spacing) {
    var seen = new Dictionary<int3, float3>();

    foreach (var t in mesh.Triangles) {
      var a = mesh.Vertices[t.x];
      var b = mesh.Vertices[t.y];
      var c = mesh.Vertices[t.z];
      var edge = math.max(math.distance(a, b), math.max(math.distance(b, c), math.distance(c, a)));
      var n = math.max(1, (int) math.ceil(edge / spacing));

      for (var i = 0; i <= n; i++) {
        for (var j = 0; i + j <= n; j++) {
          var p = a + ((b - a) * i / n) + ((c - a) * j / n);
          var key = (int3) math.floor(p / spacing);
          seen.TryAdd(key, p);
        }
      }
    }

    return [.. seen.OrderBy(kv => kv.Key.x).ThenBy(kv => kv.Key.y).ThenBy(kv => kv.Key.z).Select(kv => kv.Value)];
  }
}

internal sealed class HeapGrid {
  private readonly float2 origin;
  private readonly float cell;
  private readonly int nx;
  private readonly int nz;
  private readonly float[] top;
  private readonly Func<float2, float> ground;

  public HeapGrid(float2 min, float2 max, float cell, Func<float2, float> ground) {
    this.origin = min;
    this.cell = cell;
    this.nx = (int) math.ceil((max.x - min.x) / cell) + 1;
    this.nz = (int) math.ceil((max.y - min.y) / cell) + 1;
    this.top = Enumerable.Repeat(float.NegativeInfinity, this.nx * this.nz).ToArray();
    this.ground = ground;
  }

  public int Cells => this.top.Length;

  private int Index(float2 xz) {
    var i = (int2) math.floor((xz - this.origin) / this.cell);

    return i.x < 0 || i.y < 0 || i.x >= this.nx || i.y >= this.nz ? -1 : i.x + (i.y * this.nx);
  }

  public float Support(float2 xz, float sink) {
    var g = this.ground(xz) - sink;
    var i = this.Index(xz);

    return i < 0 ? g : math.max(g, this.top[i]);
  }

  public void Splat(float3 p) {
    var i = this.Index(p.xz);

    if (i >= 0) {
      this.top[i] = math.max(this.top[i], p.y);
    }
  }
}

internal sealed record FitOptions {
  public bool Search { get; init; } = true;
  public bool RandomStart { get; init; }
  public float Sink { get; init; } = 0.12f;
  public float Tolerance { get; init; } = 0.03f;
  public float Spring { get; init; } = 0.5f;
  public float StartTurn { get; init; } = 25f;
  public float EndTurn { get; init; } = 1.5f;
  public float StartSlide { get; init; } = 0.12f;
  public bool Slide { get; init; } = true;
  public float CellShare { get; init; } = 1f / 8f;
  public bool StableStart { get; init; }
  public int Pockets { get; init; }
  public float PocketReach { get; init; } = 0.35f;
  public bool OverGround { get; init; }
}

internal static class SettleFit {
  public static long Evaluations;

  public static List<LaidStone> Fit(
    IReadOnlyList<LaidStone> stones,
    IReadOnlyList<FitShape> shapes,
    Func<float2, float> ground,
    FitOptions options,
    uint seed
  ) {
    var random = new Random(math.max(1u, seed * 7919u));
    var meanSize = shapes.Average(s => s.Size);
    var meanNominal = shapes.Average(s => s.Nominal);
    var min = new float2(float.MaxValue);
    var max = new float2(float.MinValue);

    foreach (var s in stones) {
      min = math.min(min, s.Position.xz);
      max = math.max(max, s.Position.xz);
    }

    var poses = options.StableStart ? shapes.Select(s => s.Poses).ToList() : null;
    var grid = new HeapGrid(min - (2f * meanSize), max + (2f * meanSize), options.CellShare * meanNominal, ground);
    var fitted = new List<LaidStone>(stones.Count);

    foreach (var stone in stones) {
      var shape = shapes[stone.Shape];
      var rotation = poses is not null
        ? math.mul(quaternion.RotateY(random.NextFloat(2f * math.PI)), poses[stone.Shape][random.NextInt(poses[stone.Shape].Length)])
        : options.RandomStart ? RandomRotation(ref random) : stone.Rotation;
      var com = stone.Position.xz + math.rotate(stone.Rotation, shape.Centroid).xz;
      var sink = options.Sink * shape.Nominal;
      var tolerance = options.Tolerance * shape.Nominal;

      float Energy(quaternion r, float2 c, out float3 pivot) {
        Evaluations++;
        var offset = math.rotate(r, shape.Centroid);
        var pxz = c - offset.xz;
        var y = float.MinValue;

        foreach (var p in shape.Probe) {
          var w = math.rotate(r, p);
          y = math.max(y, grid.Support(pxz + w.xz, sink) - w.y);
        }

        pivot = new float3(pxz.x, y - tolerance, pxz.y);

        return pivot.y + offset.y - (options.OverGround ? ground(c) : 0f) + (options.Spring * math.lengthsq(c - com) / shape.Nominal);
      }

      var best = Energy(rotation, com, out var bestPivot);
      var bestCom = com;

      // The lowest of a few spots round the place, each with its own yaw.
      for (var k = 0; k < options.Pockets; k++) {
        var angle = (2f * math.PI * k / options.Pockets) + random.NextFloat(0.5f);
        var c = com + (options.PocketReach * shape.Nominal * new float2(math.cos(angle), math.sin(angle)));
        var r = math.mul(quaternion.RotateY(random.NextFloat(2f * math.PI)), rotation);
        var e = Energy(r, c, out var pv);

        if (e < best) {
          best = e;
          bestCom = c;
          bestPivot = pv;
          rotation = r;
        }
      }

      if (options.Search) {
        var turn = math.radians(options.StartTurn);
        var slide = options.StartSlide * shape.Nominal;

        while (turn >= math.radians(options.EndTurn)) {
          var improved = false;
          var candRot = rotation;
          var candCom = bestCom;
          var candPivot = bestPivot;
          var cand = best;

          for (var m = 0; m < (options.Slide ? 10 : 6); m++) {
            var r = rotation;
            var c = bestCom;

            switch (m) {
              case < 6:
                var axis = (m / 2) switch {
                  0 => new float3(1, 0, 0),
                  1 => new float3(0, 0, 1),
                  _ => new float3(0, 1, 0)
                };
                r = math.normalize(math.mul(quaternion.AxisAngle(axis, (m % 2 == 0 ? 1 : -1) * turn), rotation));
                break;
              default:
                c += new float2(m == 6 ? slide : m == 7 ? -slide : 0, m == 8 ? slide : m == 9 ? -slide : 0);
                break;
            }

            var e = Energy(r, c, out var pv);

            if (e < cand - 1e-5f) {
              cand = e;
              candRot = r;
              candCom = c;
              candPivot = pv;
              improved = true;
            }
          }

          if (improved) {
            best = cand;
            rotation = candRot;
            bestCom = candCom;
            bestPivot = candPivot;
          } else {
            turn *= 0.5f;
            slide *= 0.5f;
          }
        }
      }

      foreach (var p in shape.Fine) {
        grid.Splat(bestPivot + math.rotate(rotation, p));
      }

      fitted.Add(stone with {
        Position = bestPivot,
        Rotation = rotation
      });
    }

    return fitted;
  }

  /// <summary>The orientations the shape comes to rest in on level ground, from random starts.</summary>
  public static quaternion[] StablePoses(FitShape shape) {
    var random = new Random(12345u);
    var poses = new List<quaternion>();

    float Height(quaternion r) {
      var low = float.MaxValue;

      foreach (var p in shape.Probe) {
        low = math.min(low, math.rotate(r, p).y);
      }

      return math.rotate(r, shape.Centroid).y - low;
    }

    for (var k = 0; k < 24; k++) {
      var r = RandomRotation(ref random);
      var best = Height(r);
      var turn = math.radians(30f);

      while (turn > math.radians(0.5f)) {
        var improved = false;

        for (var m = 0; m < 4; m++) {
          var axis = m < 2 ? new float3(1, 0, 0) : new float3(0, 0, 1);
          var c = math.normalize(math.mul(quaternion.AxisAngle(axis, (m % 2 == 0 ? 1 : -1) * turn), r));
          var h = Height(c);

          if (h < best - 1e-5f) {
            best = h;
            r = c;
            improved = true;
          }
        }

        if (!improved) {
          turn *= 0.5f;
        }
      }

      poses.Add(r);
    }

    return [.. poses];
  }

  public static quaternion RandomRotation(ref Random random) {
    // Uniform over rotations (Shoemake).
    var u = random.NextFloat3();
    var a = math.sqrt(1f - u.x);
    var b = math.sqrt(u.x);

    return new quaternion(
      a * math.sin(2f * math.PI * u.y),
      a * math.cos(2f * math.PI * u.y),
      b * math.sin(2f * math.PI * u.z),
      b * math.cos(2f * math.PI * u.z)
    );
  }
}
