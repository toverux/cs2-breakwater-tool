// SPIKE (ticket 04), throwaway: measures a heap independently of the fitter, on voxels.
using System;
using System.Collections.Generic;
using System.Linq;
using BreakwaterTool.Layout;
using Unity.Mathematics;

namespace BreakwaterTool.Tests.FitSpike;

internal static class HeapMetrics {
  /// <summary>A mesh's solid voxels' centres, in its frame: the surface's cells and all they enclose.</summary>
  public static float3[] Solid(SpikeMesh mesh, float voxel) {
    var surface = FitShape.Sample(mesh, voxel * 0.4f);
    var lo = surface.Aggregate(new float3(float.MaxValue), math.min) - voxel;
    var hi = surface.Aggregate(new float3(float.MinValue), math.max) + voxel;
    var n = (int3) math.ceil((hi - lo) / voxel) + 1;
    var wall = new bool[n.x * n.y * n.z];
    int Id(int3 i) => i.x + (n.x * (i.y + (n.y * i.z)));

    foreach (var p in surface) {
      wall[Id((int3) math.floor((p - lo) / voxel))] = true;
    }

    var outside = new bool[wall.Length];
    var queue = new Queue<int3>();
    queue.Enqueue(int3.zero);
    outside[0] = true;
    int3[] steps = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];

    while (queue.Count > 0) {
      var c = queue.Dequeue();

      foreach (var d in steps) {
        var m = c + d;

        if (math.any(m < 0) || math.any(m >= n) || outside[Id(m)] || wall[Id(m)]) {
          continue;
        }

        outside[Id(m)] = true;
        queue.Enqueue(m);
      }
    }

    var solid = new List<float3>();

    for (var z = 0; z < n.z; z++) {
      for (var y = 0; y < n.y; y++) {
        for (var x = 0; x < n.x; x++) {
          if (!outside[Id(new int3(x, y, z))]) {
            solid.Add(lo + ((new float3(x, y, z) + 0.5f) * voxel));
          }
        }
      }
    }

    return [.. solid];
  }

  public sealed record Result(float Coverage, float Overlap, float Floating, float Thickness, float Stacked) {
    public override string ToString() =>
      $"cover {this.Coverage:P0} overlap {this.Overlap:P1} floating {this.Floating:P0} thick {this.Thickness:F2} stacked {this.Stacked:P0}";
  }

  public static Result Measure(
    IReadOnlyList<LaidStone> stones,
    IReadOnlyList<float3[]> solids,
    float voxel,
    float2[] outline,
    Func<float2, float> ground,
    float meanSize
  ) {
    var owners = new Dictionary<int3, int>();
    var shared = new HashSet<int3>();
    var columns = new Dictionary<int2, List<(int Y, int Unit)>>();
    var unitVoxels = new List<HashSet<int3>>();

    for (var u = 0; u < stones.Count; u++) {
      var own = new HashSet<int3>();

      foreach (var p in solids[stones[u].Shape]) {
        var w = stones[u].Position + math.rotate(stones[u].Rotation, p);
        var key = (int3) math.floor(w / voxel);

        // Under the ground does not show and is not an overlap.
        if (w.y < ground(w.xz) || !own.Add(key)) {
          continue;
        }

        if (owners.TryGetValue(key, out var other) && other != u) {
          shared.Add(key);
        } else {
          owners[key] = u;
        }

        var col = key.xz;

        if (!columns.TryGetValue(col, out var list)) {
          columns[col] = list = [];
        }

        list.Add((key.y, u));
      }

      unitVoxels.Add(own);
    }

    var filled = owners.Count;

    // Coverage and thickness over the outline's cells.
    var lo = outline.Aggregate(new float2(float.MaxValue), math.min);
    var hi = outline.Aggregate(new float2(float.MinValue), math.max);
    var inside = 0;
    var covered = 0;
    var thick = 0f;

    for (var x = lo.x + (voxel / 2); x < hi.x; x += voxel) {
      for (var z = lo.y + (voxel / 2); z < hi.y; z += voxel) {
        var at = new float2(x, z);

        if (!Contains(outline, at)) {
          continue;
        }

        inside++;
        var col = (int2) math.floor(at / voxel);

        if (columns.TryGetValue(col, out var list)) {
          covered++;
          thick += (((list.Max(e => e.Y) + 1) * voxel) - ground(at)) / meanSize;
        }
      }
    }

    // A unit floats when no voxel of it lies on the ground or right over another unit's.
    var floating = 0;
    var stacked = 0;

    for (var u = 0; u < stones.Count; u++) {
      var gap = float.MaxValue;
      var onGround = false;

      foreach (var key in unitVoxels[u]) {
        var y = key.y * voxel;
        var g = ground(((float2) key.xz + 0.5f) * voxel);
        var below = g;

        foreach (var (oy, ou) in columns[key.xz]) {
          if (ou != u && oy < key.y) {
            below = math.max(below, (oy + 1) * voxel);
          }
        }

        gap = math.min(gap, y - below);
        onGround |= y - g < 1.5f * voxel;
      }

      if (gap > 1.5f * voxel) {
        floating++;
      }

      if (!onGround) {
        stacked++;
      }
    }

    var n = math.max(1, stones.Count);

    return new Result(
      covered / (float) math.max(1, inside),
      shared.Count / (float) math.max(1, filled),
      floating / (float) n,
      thick / math.max(1, covered),
      stacked / (float) n
    );
  }

  private static bool Contains(float2[] polygon, float2 p) {
    var inside = false;

    for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++) {
      if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
        p.x < ((polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y)) + polygon[i].x) {
        inside = !inside;
      }
    }

    return inside;
  }
}
