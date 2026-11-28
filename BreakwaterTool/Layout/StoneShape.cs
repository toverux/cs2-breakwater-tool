using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace BreakwaterTool.Layout;

/// <summary>
/// A stone prefab's geometry from its mesh: the turn about the vertical that brings its long axis
/// onto x, the long side of the smallest rectangle round it seen from above, and its vertices in
/// that frame.
/// </summary>
internal sealed class StoneShape {
  private readonly float3[] vertices;

  // Built on the first dumped layout that lays the stone, as a laid breakwater never needs it.
  private readonly System.Lazy<HeapShape> heap;

  /// <param name="mesh">The prefab's vertices in its own frame, all its meshes together.</param>
  /// <param name="triangles">
  /// Three indices into the vertices per triangle, which fitting a dumped stone samples; without
  /// them, it takes the vertices alone.
  /// </param>
  internal StoneShape(IReadOnlyList<float3> mesh, IReadOnlyList<int>? triangles = null) {
    var original = mesh.ToArray();

    this.heap = new System.Lazy<HeapShape>(() => new HeapShape(original, triangles ?? []));

    var smallest = float.MaxValue;

    for (var degree = 0; degree < 90; degree++) {
      var extent = StoneShape.Extent(mesh, math.radians(degree), float.MinValue, out _);

      if (extent.x * extent.y >= smallest) {
        continue;
      }

      smallest = extent.x * extent.y;

      this.Angle = math.radians(degree) + (extent.y > extent.x ? math.PI / 2f : 0f);
    }

    var turn = quaternion.RotateY(this.Angle);

    this.vertices = new float3[mesh.Count];
    this.MinY = float.MaxValue;
    this.MaxY = float.MinValue;

    for (var i = 0; i < mesh.Count; i++) {
      this.vertices[i] = math.rotate(turn, mesh[i]);
      this.MinY = math.min(this.MinY, this.vertices[i].y);
      this.MaxY = math.max(this.MaxY, this.vertices[i].y);
    }
  }

  /// <summary>
  /// The turn about the vertical, in radians, from the prefab's frame to the long axis frame.
  /// </summary>
  internal float Angle { get; }

  internal float MinY { get; }

  internal float MaxY { get; }

  internal float Height => this.MaxY - this.MinY;

  /// <summary>
  /// What fitting the stone into a heap takes from its mesh.
  /// </summary>
  internal HeapShape Heap => this.heap.Value;

  /// <summary>
  /// The size and centre, in the long axis frame, of the part above the embed line: what shows
  /// above the ground.
  /// </summary>
  internal float2 Footprint(float embed, out float2 centre) =>
    StoneShape.Extent(this.vertices, 0f, this.MinY + (embed * this.Height), out centre);

  /// <summary>
  /// The size and centre, seen from above and turned by an angle, of the vertices above a height.
  /// </summary>
  private static float2 Extent(
    IReadOnlyList<float3> points,
    float angle,
    float above,
    out float2 centre
  ) {
    var turn = quaternion.RotateY(angle);
    var min = new float2(float.MaxValue);
    var max = new float2(float.MinValue);

    foreach (var point in points) {
      if (point.y < above) {
        continue;
      }

      var turned = math.rotate(turn, point);

      min = math.min(min, turned.xz);
      max = math.max(max, turned.xz);
    }

    centre = (min + max) * 0.5f;

    return max - min;
  }
}
