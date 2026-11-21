using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BreakwaterTool.Layout;
using Unity.Mathematics;

namespace BreakwaterTool.Scenarios.Ground;

/// <summary>
/// The sites of the proving ground, one plot each, side by side along the coast.
/// A formula site's local frame has its origin on the shore line in the middle of its plot, u along
/// the coast and v towards the land.
/// The two Nice sites are patches of the heights of the "Nice 22" map, under their outlines.
/// The heights derive from IGN RGE ALTI and SHOM Litto3D (Licence Ouverte), and the Lympia outline
/// from OpenStreetMap (ODbL, © OpenStreetMap contributors).
/// </summary>
internal static class ProvingSites {
  /// <summary>
  /// A formula site's plot, in posts: 630 m.
  /// </summary>
  private const int Plot = 180;

  /// <summary>
  /// The posts between two plots.
  /// </summary>
  private const int Gap = 8;

  /// <summary>
  /// The first plot's first post along x.
  /// </summary>
  private const int FirstPlot = 640;

  /// <summary>
  /// What moves the Nice patches' sea, at 99.95 m, onto the ground's: a patch's value times 16, the
  /// ratio of its height scale to the ground's, less this many of the ground's steps.
  /// </summary>
  private const int NiceShift = 5107;

  /// <summary>
  /// The crest of a jetty's ridge over the sea, and half its width.
  /// </summary>
  private const double Crest = 3;

  internal static IReadOnlyList<ProvingSite> All { get; } = ProvingSites.Build();

  internal static ProvingSite Named(string name) =>
    ProvingSites.All.Single(site => site.Name == name);

  private static List<ProvingSite> Build() {
    var row = new PlotRow();

    (double U, double V)[] jetty = [(0, -4), (0, -30), (0, -120)];
    (double U, double V)[] bend = [(0, -4), (0, -30), .. ProvingSites.Arc(), (100, -100)];

    return [
      row.Place(
        new Formula {
          Name = "revetment",
          Shows = "a bank falling one in two from the land into the sea",
          Stones = SiteStones.Boulders,
          Relief = static (_, v) => ProvingGround.Coast(v),
          Outline = [(-30, -16), (30, -16), (30, 6), (-30, 6)]
        }
      ),
      row.Place(
        new Formula {
          Name = "corner-toe",
          Shows = "a slope falling across the outline, whose low part is a corner",
          Stones = SiteStones.Crates,
          Relief = static (u, v) => ProvingGround.SeaFloor + (0.1 * (20 - u)) + (0.3 * (v + 5)),
          Outline = [(-20, -5), (20, -5), (20, 5), (-20, 5)]
        }
      ),
      row.Place(
        new Formula {
          Name = "level",
          Shows = "level ground, every edge a toe",
          Stones = SiteStones.Boulders,
          Relief = static (_, _) => ProvingGround.Land,
          Outline = [(-15, 10), (15, 10), (15, 30), (-15, 30)]
        }
      ),
      row.Place(
        new Formula {
          Name = "jetty",
          Shows = "a ridge out to sea, rows up both flanks to the spine and round the head",
          Stones = SiteStones.Boulders,
          Relief = (u, v) => ProvingSites.Jetty(u, v, jetty),
          Outline = ProvingSites.Band(jetty, 24)
        }
      ),
      row.Place(
        new Formula {
          Name = "bend",
          Shows = "a jetty turning a quarter circle, rows fanning out and closing up",
          Stones = SiteStones.Boulders,
          Relief = (u, v) => ProvingSites.Jetty(u, v, bend),
          Outline = ProvingSites.Band(bend, 24)
        }
      ),
      row.Place(
        new Formula {
          Name = "inlet",
          Shows = "a narrow notch up the bank, which the rows break across",
          Stones = SiteStones.Crates,
          Relief = static (_, v) => ProvingGround.Coast(v),
          Outline = [(-30, -16), (-2, -16), (-2, 2), (2, 2), (2, -16), (30, -16), (30, 6), (-30, 6)]
        }
      ),
      row.Place(
        new Formula {
          Name = "steep",
          Shows = "a slope of one in one, whose rows outnumber the guard's estimate",
          Stones = SiteStones.Crates,
          Relief = static (_, v) => Math.Min(Math.Max(ProvingGround.SeaFloor + v + 70, 70), 210),
          Outline = [(-95, -70), (95, -70), (95, 70), (-95, 70)],
          Verdict = LayoutVerdict.OverGuard
        }
      ),
      row.Place(
        new Formula {
          Name = "oversize",
          Shows = "an outline whose area alone is over the guard",
          Stones = SiteStones.Crates,
          Relief = static (_, _) => ProvingGround.Land,
          Outline = [(-150, -150), (150, -150), (150, 150), (-150, 150)],
          Verdict = LayoutVerdict.OverGuard
        }
      ),
      row.Place(
        new Formula {
          Name = "noise",
          Shows = "rough ground under an irregular outline",
          Stones = SiteStones.Boulders,
          Relief = static (u, v) => ProvingGround.Coast(v) + ProvingSites.Noise(u, v),
          Outline = ProvingSites.Star()
        }
      ),
      row.Place(
        new Formula {
          Name = "near-guard",
          Shows = "some 16 000 to 18 000 stones on a gentle slope, under the guard",
          Stones = SiteStones.Crates,
          Relief = static (_, v) => ProvingGround.SeaFloor + (0.1 * (v + 60)),
          Outline = [(-120, -60), (120, -60), (120, 40), (-120, 40)]
        }
      ),
      row.Place(
        new Formula {
          Name = "long-diagonal",
          Shows = "a jetty 2 km long and 10 m wide drawn on the diagonal, a vast bounding box",
          Stones = SiteStones.Crates,
          Posts = 420,
          Relief = static (_, _) => ProvingGround.SeaFloor,
          Outline = ProvingSites.Diagonal()
        }
      ),
      row.Place(
        new Patch {
          Name = "lympia",
          Shows = "the Port Lympia jetty in Nice, on its real ground",
          NiceFirst = new int2(2517, 1355),
          Size = new int2(148, 147),
          Outline =
            "1901.2,-2068.5 1898.3,-2075.5 1976.1,-2159.5 2085.7,-2348.5 2082.5,-2353.4 " +
            "2048.8,-2310.0 1985.6,-2191.0 1928.5,-2120.5 1900.5,-2097.6 1890.0,-2095.8 " +
            "1858.5,-2068.0 1823.5,-2050.2 1771.0,-2031.6 1714.2,-1996.1 1729.5,-1985.6 " +
            "1743.5,-1985.9 1837.8,-2025.0 1888.1,-2054.2"
        }
      ),
      row.Place(
        new Patch {
          Name = "user-hook",
          Shows = "a hooked jetty the user drew in Nice, on its real ground",
          NiceFirst = new int2(3788, 1622),
          Size = new int2(103, 162),
          Outline =
            "6169.0,-1405.9 6181.8,-1417.9 6196.1,-1414.6 6210.5,-1398.5 6236.8,-1348.0 " +
            "6267.5,-1300.7 6316.9,-1223.4 6355.9,-1159.0 6378.7,-1116.7 6374.4,-1090.3 " +
            "6363.9,-1039.6 6346.1,-1015.0 6318.2,-1002.8 6297.1,-999.1 6275.9,-1018.3 " +
            "6276.7,-1026.2 6294.2,-1041.7 6320.8,-1059.2 6332.7,-1090.6 6335.5,-1115.9 " +
            "6332.7,-1132.6 6283.8,-1215.4 6242.7,-1284.4 6186.0,-1379.1 6180.9,-1388.9 " +
            "6171.8,-1390.8 6165.2,-1387.0 6163.5,-1396.3"
        }
      )
    ];
  }

  /// <summary>
  /// A ridge along a spine, its crest <see cref="Crest"/> over the sea and as wide each side, its
  /// flanks falling two in three to the sea floor, standing on the coast.
  /// </summary>
  private static double Jetty(double u, double v, (double U, double V)[] spine) {
    var distance = double.MaxValue;

    for (var i = 1; i < spine.Length; i++) {
      distance = Math.Min(distance, ProvingSites.Distance((u, v), spine[i - 1], spine[i]));
    }

    var ridge = ProvingGround.SeaLevel +
      ProvingSites.Crest -
      (Math.Max(0, distance - ProvingSites.Crest) / 1.5);

    return Math.Max(ProvingGround.Coast(v), ridge);
  }

  private static double Distance(
    (double U, double V) point,
    (double U, double V) a,
    (double U, double V) b
  ) {
    var abU = b.U - a.U;
    var abV = b.V - a.V;
    var along = (((point.U - a.U) * abU) + ((point.V - a.V) * abV)) / ((abU * abU) + (abV * abV));
    var t = Math.Min(Math.Max(along, 0), 1);
    var dU = point.U - a.U - (t * abU);
    var dV = point.V - a.V - (t * abV);

    return Math.Sqrt((dU * dU) + (dV * dV));
  }

  /// <summary>
  /// An outline a half width each side of a spine, its far end closed by a point as far again past
  /// the spine's end, where the ridge is down to the sea floor.
  /// </summary>
  private static (double U, double V)[] Band((double U, double V)[] spine, double halfWidth) {
    var left = new List<(double U, double V)>();
    var right = new List<(double U, double V)>();
    (double U, double V) tangent = (0, 0);

    for (var i = 0; i < spine.Length; i++) {
      var from = spine[Math.Max(i - 1, 0)];
      var to = spine[Math.Min(i + 1, spine.Length - 1)];
      var length = Math.Sqrt(
        ((to.U - from.U) * (to.U - from.U)) + ((to.V - from.V) * (to.V - from.V))
      );

      tangent = ((to.U - from.U) / length, (to.V - from.V) / length);
      left.Add((spine[i].U - (tangent.V * halfWidth), spine[i].V + (tangent.U * halfWidth)));
      right.Add((spine[i].U + (tangent.V * halfWidth), spine[i].V - (tangent.U * halfWidth)));
    }

    var end = spine[^1];

    right.Reverse();

    return [.. left, (end.U + (tangent.U * halfWidth), end.V + (tangent.V * halfWidth)), .. right];
  }

  /// <summary>
  /// The bend's quarter circle of 60 m radius, turning left from heading -v to heading +u, by steps
  /// of 15°.
  /// </summary>
  private static IEnumerable<(double U, double V)> Arc() {
    // Turned by 15° each step, its cosine and sine from square roots.
    var cos = (Math.Sqrt(6) + Math.Sqrt(2)) / 4;
    var sin = (Math.Sqrt(6) - Math.Sqrt(2)) / 4;
    (double U, double V) radius = (-1, 0);

    for (var step = 0; step <= 6; step++) {
      yield return (60 + (60 * radius.U), -40 + (60 * radius.V));

      radius = ((radius.U * cos) - (radius.V * sin), (radius.U * sin) + (radius.V * cos));
    }
  }

  /// <summary>
  /// Twelve points round the waterline at random distances from 22.5 m to 37.5 m, every 30°.
  /// </summary>
  private static (double U, double V)[] Star() {
    var cos = Math.Sqrt(3) / 2;
    var points = new (double U, double V)[12];
    (double U, double V) direction = (1, 0);

    for (var k = 0; k < points.Length; k++) {
      var radius = 30 * (0.75 + (0.5 * ProvingSites.Unit(ProvingSites.Hash(k, 0, 7))));

      points[k] = (radius * direction.U, -6 + (radius * direction.V));
      direction = ((direction.U * cos) - (direction.V * 0.5),
        (direction.U * 0.5) + (direction.V * cos));
    }

    return points;
  }

  /// <summary>
  /// A rectangle 2000 m by 10 m centred on the plot's origin, its length along u = v.
  /// </summary>
  private static (double U, double V)[] Diagonal() {
    var half = Math.Sqrt(0.5);

    (double U, double V) along = (1000 * half, 1000 * half);
    (double U, double V) across = (-5 * half, 5 * half);

    return [
      (-along.U - across.U, -along.V - across.V),
      (along.U - across.U, along.V - across.V),
      (along.U + across.U, along.V + across.V),
      (-along.U + across.U, -along.V + across.V)
    ];
  }

  /// <summary>
  /// Value noise up to 1.5 m each way, in two octaves of 12 m and 5 m.
  /// </summary>
  private static double Noise(double u, double v) =>
    (1.0 * ProvingSites.Lattice(u, v, 12, 1)) + (0.5 * ProvingSites.Lattice(u, v, 5, 2));

  /// <summary>
  /// Random values from -1 to 1 at the corners of a square lattice, blended smoothly between.
  /// </summary>
  private static double Lattice(double u, double v, double step, uint seed) {
    var cellU = Math.Floor(u / step);
    var cellV = Math.Floor(v / step);
    var i = (int) cellU;
    var j = (int) cellV;
    var tU = ProvingSites.Smooth((u / step) - cellU);
    var tV = ProvingSites.Smooth((v / step) - cellV);

    var below = ProvingSites.Lerp(Corner(0, 0), Corner(1, 0), tU);
    var above = ProvingSites.Lerp(Corner(0, 1), Corner(1, 1), tU);

    return ProvingSites.Lerp(below, above, tV);

    double Corner(int di, int dj) =>
      (2 * ProvingSites.Unit(ProvingSites.Hash(i + di, j + dj, seed))) - 1;
  }

  private static double Smooth(double t) => t * t * (3 - (2 * t));

  private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

  private static double Unit(uint hash) => hash / (double) uint.MaxValue;

  private static uint Hash(int x, int z, uint seed) {
    var hash = seed ^ unchecked((uint) x * 0x9E3779B1u) ^ unchecked((uint) z * 0x85EBCA77u);

    hash ^= hash >> 16;
    hash = unchecked(hash * 0x7FEB352Du);
    hash ^= hash >> 15;
    hash = unchecked(hash * 0x846CA68Bu);
    hash ^= hash >> 16;

    return hash;
  }

  /// <summary>
  /// A site whose ground is a formula of its local frame.
  /// </summary>
  private sealed class Formula {
    internal required string Name { get; init; }

    internal required string Shows { get; init; }

    internal required SiteStones Stones { get; init; }

    /// <summary>
    /// The ground's height at a point of the local frame, in metres.
    /// </summary>
    internal required Func<double, double, double> Relief { get; init; }

    internal required (double U, double V)[] Outline { get; init; }

    /// <summary>
    /// The plot's side, in posts.
    /// </summary>
    internal int Posts { get; init; } = ProvingSites.Plot;

    internal LayoutVerdict Verdict { get; init; } = LayoutVerdict.Laid;
  }

  /// <summary>
  /// A site whose ground is a patch of the "Nice 22" map, a resource of the assembly, its outline
  /// in that map's metres.
  /// </summary>
  private sealed class Patch {
    internal required string Name { get; init; }

    internal required string Shows { get; init; }

    /// <summary>
    /// The patch's first post in the Nice map.
    /// </summary>
    internal required int2 NiceFirst { get; init; }

    internal required int2 Size { get; init; }

    /// <summary>
    /// The outline in the Nice map, "x,z x,z ..." in metres.
    /// </summary>
    internal required string Outline { get; init; }
  }

  /// <summary>
  /// Places plots one after the other along x, centred on the shore line.
  /// </summary>
  private sealed class PlotRow {
    // Where the next plot starts, moved on by each.
    private int next = ProvingSites.FirstPlot;

    internal ProvingSite Place(Formula site) {
      var posts = new int2(site.Posts);
      var first = this.Take(posts);
      var origin = (first.x + (site.Posts / 2) - (ProvingGround.Resolution / 2)) *
        ProvingGround.CellSize;

      return new ProvingSite {
        Name = site.Name,
        Shows = site.Shows,
        FirstPost = first,
        Posts = posts,
        Stones = site.Stones,
        Verdict = site.Verdict,
        Height = post => ProvingGround.Encode(
          site.Relief(ProvingGround.World(post.x) - origin, ProvingGround.World(post.y))
        ),
        Outline = [
          .. site.Outline.Select(point => new float2((float) (origin + point.U), (float) point.V))
        ]
      };
    }

    internal ProvingSite Place(Patch site) {
      var first = this.Take(site.Size);
      var heights = PlotRow.Load(site.Name, site.Size);
      var shiftX = (first.x - site.NiceFirst.x) * ProvingGround.CellSize;
      var shiftZ = (first.y - site.NiceFirst.y) * ProvingGround.CellSize;

      return new ProvingSite {
        Name = site.Name,
        Shows = site.Shows,
        FirstPost = first,
        Posts = site.Size,
        Stones = SiteStones.Boulders,
        Height = post => {
          var local = post - first;

          return (ushort) ((16 * heights[(local.y * site.Size.x) + local.x]) -
            ProvingSites.NiceShift);
        },
        Outline = [
          .. site.Outline.Split(' ')
            .Select(static point => point.Split(','))
            .Select(xz => new float2(
                (float) (double.Parse(xz[0], CultureInfo.InvariantCulture) + shiftX),
                (float) (double.Parse(xz[1], CultureInfo.InvariantCulture) + shiftZ)
              )
            )
        ]
      };
    }

    private int2 Take(int2 posts) {
      var first = new int2(this.next, (ProvingGround.Resolution / 2) - (posts.y / 2));

      this.next += posts.x + ProvingSites.Gap;

      return first;
    }

    /// <summary>
    /// A patch's posts, little-endian 16-bit values row after row of growing z.
    /// </summary>
    private static ushort[] Load(string name, int2 size) {
      using var stream =
        typeof(ProvingSites).Assembly.GetManifestResourceStream($"Ground.{name}") ??
        throw new InvalidOperationException($"The patch {name} is not a resource of the assembly.");

      using var reader = new BinaryReader(stream);

      var heights = new ushort[size.x * size.y];

      for (var i = 0; i < heights.Length; i++) {
        heights[i] = reader.ReadUInt16();
      }

      return heights;
    }
  }
}
