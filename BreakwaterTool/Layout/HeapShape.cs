using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

namespace BreakwaterTool.Layout;

/// <summary>
/// What fitting a dumped stone among others takes from its mesh, whatever the prop: points on its
/// surface, the poses it comes to rest in on level ground, and the room it takes once at rest.
/// </summary>
internal sealed class HeapShape {
  /// <summary>
  /// The level-ground settles that find the stone's resting poses.
  /// </summary>
  private const int PoseStarts = 24;

  /// <summary>
  /// The fewest and the most points a drop is tested on: more for a long, thin stone, whose ends
  /// are far apart.
  /// </summary>
  private const int FewestProbes = 48;

  private const int MostProbes = 160;

  /// <summary>
  /// The most points the surface is sampled with: a prop many times its nominal size, a section of
  /// a wall made of many units, is sampled coarser.
  /// </summary>
  private const int MostSurface = 12_000;

  /// <param name="vertices">The prefab's vertices in its own frame, all its meshes together.</param>
  /// <param name="triangles">Three vertex indices per triangle.</param>
  internal HeapShape(IReadOnlyList<float3> vertices, IReadOnlyList<int> triangles) {
    var lo = new float3(float.MaxValue);
    var hi = new float3(float.MinValue);

    foreach (var vertex in vertices) {
      lo = math.min(lo, vertex);
      hi = math.max(hi, vertex);
    }

    this.Size = math.cmax(hi - lo);

    var voxel = this.Size / 32f;
    var volume = HeapShape.SolidCount(vertices, triangles, voxel) * voxel * voxel * voxel;

    // A flat or open mesh encloses next to nothing: its bounds stand in.
    this.Nominal = math.max(math.pow(volume, 1f / 3f), 0.05f * this.Size);
    this.Centroid = HeapShape.SurfaceCentroid(vertices, triangles, (lo + hi) / 2f);

    var spacing = this.Nominal / 10f;
    var surface = HeapShape.Sample(vertices, triangles, spacing);

    while (surface.Length > HeapShape.MostSurface) {
      spacing *= 1.25f;
      surface = HeapShape.Sample(vertices, triangles, spacing);
    }

    this.Surface = surface;

    this.Probe = HeapShape.Farthest(
      HeapShape.Sample(vertices, triangles, math.max(spacing, this.Nominal / 6f)),
      (int) math.clamp(
        32f + (16f * this.Size / this.Nominal),
        HeapShape.FewestProbes,
        HeapShape.MostProbes
      )
    );

    this.Poses = this.StablePoses();

    // The pose most starts end in, by the direction of the stone's own frame that points down.
    this.Rest = this.Poses
      .GroupBy(static pose => (int3) math.round(HeapShape.Down(pose) * 4f))
      .OrderByDescending(static group => group.Count())
      .First()
      .First();

    this.Rested = new StoneShape([.. vertices.Select(vertex => math.rotate(this.Rest, vertex))]);

    // Seen from above at rest, the share of its smallest rectangle the stone covers.
    var cell = this.Nominal / 12f;

    var cells = this.Surface
      .Select(point => (int2) math.floor(math.rotate(this.Rest, point).xz / cell))
      .Distinct()
      .Count();

    var rectangle = this.Rested.Footprint(0f, out _);

    this.Fill = math.clamp(cells * cell * cell / (rectangle.x * rectangle.y), 0.05f, 1f);
  }

  /// <summary>
  /// The longest side of the stone's bounds, in metres.
  /// </summary>
  internal float Size { get; }

  /// <summary>
  /// The cube root of the stone's volume, in metres: what its fitting's steps and tolerances
  /// scale by, as a long, thin stone is no bigger than its girth there.
  /// </summary>
  internal float Nominal { get; }

  internal float3 Centroid { get; }

  /// <summary>
  /// Points on the surface, about a tenth of the nominal size apart: what a stone at rest adds to
  /// the heap's top.
  /// </summary>
  internal float3[] Surface { get; }

  /// <summary>
  /// Points of the surface as far apart as they go: what a drop is tested on.
  /// </summary>
  internal float3[] Probe { get; }

  /// <summary>
  /// The orientations the stone comes to rest in on level ground, from the prefab's frame.
  /// </summary>
  internal quaternion[] Poses { get; }

  /// <summary>
  /// The pose most starts come to rest in.
  /// </summary>
  internal quaternion Rest { get; }

  /// <summary>
  /// The stone turned to <see cref="Rest"/>: what the rows of a dumped breakwater are spaced by.
  /// </summary>
  internal StoneShape Rested { get; }

  /// <summary>
  /// The share of the rested footprint's rectangle the stone covers from above, up to 1.
  /// </summary>
  internal float Fill { get; }

  /// <summary>
  /// The direction of the stone's own frame that a pose turns straight down.
  /// </summary>
  internal static float3 Down(quaternion pose) =>
    math.rotate(math.inverse(pose), new float3(0f, -1f, 0f));

  /// <summary>
  /// How far the centroid stands over the stone's lowest probe in a pose.
  /// </summary>
  internal float Stand(quaternion pose) {
    var low = float.MaxValue;

    foreach (var point in this.Probe) {
      low = math.min(low, math.rotate(pose, point).y);
    }

    return math.rotate(pose, this.Centroid).y - low;
  }

  private quaternion[] StablePoses() {
    var random = new Random(0x5EED_1234u);
    var poses = new quaternion[HeapShape.PoseStarts];

    for (var k = 0; k < poses.Length; k++) {
      var pose = HeapShape.RandomRotation(ref random);
      var stand = this.Stand(pose);
      var turn = math.radians(30f);

      // Tips the stone over whichever way lowers it, in finer turns, until no turn does.
      while (turn > math.radians(0.5f)) {
        var tipped = false;

        for (var move = 0; move < 4; move++) {
          var axis = move < 2 ? new float3(1f, 0f, 0f) : new float3(0f, 0f, 1f);
          var sign = move % 2 == 0 ? 1f : -1f;
          var candidate = math.normalize(math.mul(quaternion.AxisAngle(axis, sign * turn), pose));
          var height = this.Stand(candidate);

          if (height < stand - (1e-5f * this.Size)) {
            stand = height;
            pose = candidate;
            tipped = true;
          }
        }

        if (!tipped) {
          turn *= 0.5f;
        }
      }

      poses[k] = pose;
    }

    return poses;
  }

  /// <summary>
  /// A rotation drawn uniformly among all rotations (Shoemake's method).
  /// </summary>
  internal static quaternion RandomRotation(ref Random random) {
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

  private static float3 SurfaceCentroid(
    IReadOnlyList<float3> vertices,
    IReadOnlyList<int> triangles,
    float3 fallback
  ) {
    var area = 0f;
    var sum = float3.zero;

    for (var t = 0; t + 2 < triangles.Count; t += 3) {
      var a = vertices[triangles[t]];
      var b = vertices[triangles[t + 1]];
      var c = vertices[triangles[t + 2]];
      var part = 0.5f * math.length(math.cross(b - a, c - a));

      area += part;
      sum += part * (a + b + c) / 3f;
    }

    return area > 0f ? sum / area : fallback;
  }

  /// <summary>
  /// Points on the triangles about a spacing apart, one per cell of that size, in a fixed order.
  /// Without triangles, the vertices themselves.
  /// </summary>
  private static float3[] Sample(
    IReadOnlyList<float3> vertices,
    IReadOnlyList<int> triangles,
    float spacing
  ) {
    var seen = new Dictionary<int3, float3>();

    void Add(float3 point) => seen.TryAdd((int3) math.floor(point / spacing), point);

    if (triangles.Count < 3) {
      foreach (var vertex in vertices) {
        Add(vertex);
      }
    }

    for (var t = 0; t + 2 < triangles.Count; t += 3) {
      var a = vertices[triangles[t]];
      var b = vertices[triangles[t + 1]];
      var c = vertices[triangles[t + 2]];
      var edge = math.max(math.distance(a, b), math.max(math.distance(b, c), math.distance(c, a)));
      var n = math.max(1, (int) math.ceil(edge / spacing));

      for (var i = 0; i <= n; i++) {
        for (var j = 0; i + j <= n; j++) {
          Add(a + ((b - a) * i / n) + ((c - a) * j / n));
        }
      }
    }

    return [
      .. seen
        .OrderBy(static pair => pair.Key.z)
        .ThenBy(static pair => pair.Key.y)
        .ThenBy(static pair => pair.Key.x)
        .Select(static pair => pair.Value)
    ];
  }

  /// <summary>
  /// A subset of the points, each the farthest from those taken before, starting from the first.
  /// </summary>
  private static float3[] Farthest(float3[] points, int count) {
    if (points.Length <= count) {
      return points;
    }

    var taken = new float3[count];
    var nearest = Enumerable.Repeat(float.MaxValue, points.Length).ToArray();
    var next = 0;

    for (var k = 0; k < count; k++) {
      taken[k] = points[next];

      var farthest = -1f;

      for (var i = 0; i < points.Length; i++) {
        nearest[i] = math.min(nearest[i], math.distancesq(points[i], taken[k]));

        if (nearest[i] > farthest) {
          farthest = nearest[i];
          next = i;
        }
      }
    }

    return taken;
  }

  /// <summary>
  /// The voxels of a grid that the surface's cells enclose, those cells included: the stone's
  /// volume in voxels. Open meshes leak and enclose nothing but their surface.
  /// </summary>
  private static int SolidCount(
    IReadOnlyList<float3> vertices,
    IReadOnlyList<int> triangles,
    float voxel
  ) {
    var surface = HeapShape.Sample(vertices, triangles, voxel * 0.4f);

    if (surface.Length == 0) {
      return 0;
    }

    var lo = surface.Aggregate(new float3(float.MaxValue), math.min) - voxel;
    var hi = surface.Aggregate(new float3(float.MinValue), math.max) + voxel;
    var n = (int3) math.ceil((hi - lo) / voxel) + 1;
    var wall = new bool[n.x * n.y * n.z];

    int Index(int3 at) => at.x + (n.x * (at.y + (n.y * at.z)));

    foreach (var point in surface) {
      wall[Index(math.clamp((int3) math.floor((point - lo) / voxel), 0, n - 1))] = true;
    }

    // Floods the outside from a corner the margin keeps clear; whatever it never reaches is solid.
    var outside = new bool[wall.Length];
    var queue = new Queue<int3>();
    var reached = 1;

    outside[0] = true;
    queue.Enqueue(int3.zero);

    int3[] steps = [
      new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)
    ];

    while (queue.Count > 0) {
      var cell = queue.Dequeue();

      foreach (var step in steps) {
        var next = cell + step;

        if (math.any(next < 0) || math.any(next >= n)) {
          continue;
        }

        var index = Index(next);

        if (outside[index] || wall[index]) {
          continue;
        }

        outside[index] = true;
        reached++;
        queue.Enqueue(next);
      }
    }

    return wall.Length - reached;
  }
}
