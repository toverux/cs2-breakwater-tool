// SPIKE (ticket 04), throwaway: meshes for the fitting spike, read from OBJ or built procedurally.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Mathematics;

namespace BreakwaterTool.Tests.FitSpike;

internal sealed class SpikeMesh(string name, float3[] vertices, int3[] triangles) {
  public string Name { get; } = name;
  public float3[] Vertices { get; } = vertices;
  public int3[] Triangles { get; } = triangles;

  public static SpikeMesh Obj(string path) {
    var v = new List<float3>();
    var f = new List<int3>();

    foreach (var line in File.ReadLines(path)) {
      var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

      if (p.Length == 0) {
        continue;
      }

      if (p[0] == "v") {
        v.Add(new float3(F(p[1]), F(p[2]), F(p[3])));
      } else if (p[0] == "f") {
        f.Add(new int3(I(p[1]), I(p[2]), I(p[3])));
      }
    }

    return new SpikeMesh(Path.GetFileNameWithoutExtension(path), [.. v], [.. f]);

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    static int I(string s) => int.Parse(s.Split('/')[0], CultureInfo.InvariantCulture) - 1;
  }

  /// <summary>The connected parts of a mesh, welded by position, as meshes of their own.</summary>
  public IEnumerable<SpikeMesh> Parts() {
    var weld = new Dictionary<int3, int>();
    var id = new int[this.Vertices.Length];

    for (var i = 0; i < this.Vertices.Length; i++) {
      var key = (int3) math.round(this.Vertices[i] * 1000f);
      id[i] = weld.TryGetValue(key, out var w) ? w : weld[key] = weld.Count;
    }

    var parent = Enumerable.Range(0, weld.Count).ToArray();

    int Find(int x) {
      while (parent[x] != x) {
        x = parent[x] = parent[parent[x]];
      }

      return x;
    }

    foreach (var t in this.Triangles) {
      parent[Find(id[t.y])] = Find(id[t.x]);
      parent[Find(id[t.z])] = Find(id[t.x]);
    }

    var k = 0;

    foreach (var group in this.Triangles.GroupBy(t => Find(id[t.x]))) {
      var map = new Dictionary<int, int>();
      var verts = new List<float3>();

      int Map(int i) {
        if (!map.TryGetValue(i, out var m)) {
          m = map[i] = verts.Count;
          verts.Add(this.Vertices[i]);
        }

        return m;
      }

      var tris = group.Select(t => new int3(Map(t.x), Map(t.y), Map(t.z))).ToArray();

      yield return new SpikeMesh($"{this.Name}#{k++}", [.. verts], tris);
    }
  }

  /// <summary>The mesh moved so its bounds' bottom centre is at the origin.</summary>
  public SpikeMesh Based() {
    var lo = this.Vertices.Aggregate(new float3(float.MaxValue), math.min);
    var hi = this.Vertices.Aggregate(new float3(float.MinValue), math.max);
    var shift = new float3((lo.x + hi.x) / 2f, lo.y, (lo.z + hi.z) / 2f);

    return new SpikeMesh(this.Name, [.. this.Vertices.Select(v => v - shift)], this.Triangles);
  }

  public SpikeMesh Named(string name) => new(name, this.Vertices, this.Triangles);

  // Procedural shapes, a holdout set no tuning looks at.
  public static SpikeMesh Union(string name, IEnumerable<SpikeMesh> parts) {
    var v = new List<float3>();
    var f = new List<int3>();

    foreach (var part in parts) {
      var o = v.Count;
      v.AddRange(part.Vertices);
      f.AddRange(part.Triangles.Select(t => t + o));
    }

    return new SpikeMesh(name, [.. v], [.. f]);
  }

  /// <summary>A capped frustum from a to b, radii ra and rb.</summary>
  public static SpikeMesh Frustum(float3 a, float3 b, float ra, float rb, int sides = 12) {
    var axis = math.normalize(b - a);
    var u = math.normalize(math.cross(axis, math.abs(axis.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0)));
    var w = math.cross(axis, u);
    var v = new List<float3> { a, b };
    var f = new List<int3>();

    for (var i = 0; i < sides; i++) {
      var t = 2f * math.PI * i / sides;
      var d = (math.cos(t) * u) + (math.sin(t) * w);
      v.Add(a + (ra * d));
      v.Add(b + (rb * d));
    }

    for (var i = 0; i < sides; i++) {
      var j = (i + 1) % sides;
      var a0 = 2 + (2 * i);
      var b0 = a0 + 1;
      var a1 = 2 + (2 * j);
      var b1 = a1 + 1;
      f.Add(new int3(a0, a1, b1));
      f.Add(new int3(a0, b1, b0));
      f.Add(new int3(0, a1, a0));
      f.Add(new int3(1, b0, b1));
    }

    return new SpikeMesh("frustum", [.. v], [.. f]);
  }

  public static SpikeMesh Box(float3 size) {
    var h = size / 2f;
    var v = new List<float3>();

    for (var i = 0; i < 8; i++) {
      v.Add(new float3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
    }

    int3[] f = [
      new(0, 2, 1), new(1, 2, 3), new(4, 5, 6), new(5, 7, 6), new(0, 1, 4), new(1, 5, 4),
      new(2, 6, 3), new(3, 6, 7), new(0, 4, 2), new(2, 4, 6), new(1, 3, 5), new(3, 7, 5)
    ];

    return new SpikeMesh("box", [.. v], f);
  }

  public static SpikeMesh Tetrapod(float leg, float r0, float r1) {
    float3[] dirs = [
      math.normalize(new float3(1, 1, 1)), math.normalize(new float3(1, -1, -1)),
      math.normalize(new float3(-1, 1, -1)), math.normalize(new float3(-1, -1, 1))
    ];

    return Union("ProcTetrapod", dirs.Select(d => Frustum(float3.zero, d * leg, r0, r1)));
  }

  public static SpikeMesh Dolos(float shank, float fluke, float r) {
    var a = new float3(-shank / 2f, 0, 0);
    var b = new float3(shank / 2f, 0, 0);

    return Union(
      "ProcDolos",
      [
        Frustum(a, b, r, r),
        Frustum(a + new float3(0, -fluke / 2f, 0), a + new float3(0, fluke / 2f, 0), r * 0.9f, r * 0.7f),
        Frustum(b + new float3(0, 0, -fluke / 2f), b + new float3(0, 0, fluke / 2f), r * 0.9f, r * 0.7f)
      ]
    );
  }

  public static SpikeMesh Jack(float arm, float r) =>
    Union(
      "ProcJack",
      [
        Frustum(new float3(-arm, 0, 0), new float3(arm, 0, 0), r, r),
        Frustum(new float3(0, -arm, 0), new float3(0, arm, 0), r, r),
        Frustum(new float3(0, 0, -arm), new float3(0, 0, arm), r, r)
      ]
    );

  public SpikeMesh Scaled(float3 s) => new(this.Name, [.. this.Vertices.Select(v => v * s)], this.Triangles);
}
