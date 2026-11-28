// SPIKE (ticket 04), throwaway: lays dumped breakwaters of real meshes and fits them, for pictures.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using BreakwaterTool.Layout;
using BreakwaterTool.Tests.Layout;
using Unity.Mathematics;
using Xunit;

namespace BreakwaterTool.Tests.FitSpike;

public sealed class FitSpikeRun {
  private static string Root([CallerFilePath] string source = "") =>
    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", ".scratch", "breakwater-port", "t4-spike"));

  internal static string OutDir => Path.Combine(FitSpikeRun.Root(), "out");

  internal static readonly BreakwaterParameters Dumped = BreakwaterParameters.Default with {
    Tumble = 0.35f,
    Irregularity = 0.25f,
    TurnJitter = 180f,
    SizeTolerance = 100f,
    Mixing = 1f,
    LevelTops = 0f
  };

  internal static Dictionary<string, SpikeMesh[]> Sets() {
    var meshes = Path.Combine(FitSpikeRun.Root(), "meshes");
    SpikeMesh Obj(string name) => SpikeMesh.Obj(Path.Combine(meshes, name + ".obj"));
    SpikeMesh[] Range(string stem, int count) => [.. Enumerable.Range(1, count).Select(i => Obj($"{stem}{i:00}"))];

    return new Dictionary<string, SpikeMesh[]> {
      ["boulders"] = Range("Boulder", 8),
      ["rocks"] = Range("Rock", 30),
      ["tetrapod"] = [Obj("Tetrapod")],
      ["tetra-small"] = [Obj("Tetrapod one piece - small")],
      ["korean-unit"] = [Obj("CS Korean Style Sea Tetrapod 10m A").Parts().First().Based().Named("KoreanUnit")],
      ["standing"] = Range("StandingStone", 8),
      ["logs"] = Range("Log", 18),
      ["barrier"] = Range("ConcreteBarrier", 3),
      ["barrel"] = Range("Barrel", 3),
      ["crate"] = Range("Crate", 3),
      ["heads"] = Range("StoneHead", 3),
      ["proc-tetrapod"] = [SpikeMesh.Tetrapod(2f, 0.75f, 0.45f).Based().Named("ProcTetrapod")],
      ["proc-dolos"] = [SpikeMesh.Dolos(3f, 3f, 0.4f).Based().Named("ProcDolos")],
      ["proc-jack"] = [SpikeMesh.Jack(1.5f, 0.35f).Based().Named("ProcJack")],
      ["proc-cube"] = [SpikeMesh.Box(new float3(2f, 1.8f, 2.1f)).Based().Named("ProcCube")]
    };
  }

  internal static (float2[] Outline, Func<float2, float> Ground) Site(string site, float s) => site switch {
    "slope" => ([new(0, 0), new(10 * s, 0), new(10 * s, 5 * s), new(0, 5 * s)], xz => 0.5f * xz.y),
    "jetty" => ([new(-4 * s, 0), new(4 * s, 0), new(4 * s, 10 * s), new(-4 * s, 10 * s)],
      xz => math.max(0f, 0.6f * ((3.2f * s) - math.abs(xz.x)))),
    _ => ([new(0, 0), new(7 * s, 0), new(7 * s, 7 * s), new(0, 7 * s)], _ => 0f)
  };

  internal static Dictionary<string, FitOptions?> Variants() => new() {
    ["tumble"] = null,

    ["pocket-wide"] = new FitOptions { StableStart = true, Spring = 1f, StartTurn = 12f, StartSlide = 0.06f, Pockets = 12, PocketReach = 0.5f },
    ["over-ground"] = new FitOptions { StableStart = true, Spring = 1f, StartTurn = 12f, StartSlide = 0.06f, Pockets = 12, PocketReach = 0.5f, OverGround = true }
  };

  [Fact]
  public void Run() {
    Assert.SkipUnless(Environment.GetEnvironmentVariable("BWT_FIT_SPIKE") is not null, "spike");

    var only = Environment.GetEnvironmentVariable("BWT_FIT_SETS")?.Split(',');
    var onlySites = Environment.GetEnvironmentVariable("BWT_FIT_SITES")?.Split(',') ?? ["slope", "jetty"];
    var out_ = Path.Combine(FitSpikeRun.Root(), "out");
    var meshOut = Path.Combine(out_, "meshes");
    Directory.CreateDirectory(meshOut);
    var report = new StringBuilder();

    foreach (var (set, meshes) in FitSpikeRun.Sets()) {
      if (only is not null && !only.Contains(set)) {
        continue;
      }

      foreach (var m in meshes) {
        FitSpikeRun.WriteObj(Path.Combine(meshOut, m.Name + ".obj"), m);
      }

      var shapes = meshes.Select(m => new StoneShape(m.Vertices)).ToList();
      var fits = meshes.Select(FitShape.From).ToList();
      var s = fits.Average(f => f.Size);
      var voxel = fits.Average(f => f.Nominal) / 10f;
      var restShapes = meshes.Select((m, i) => {
        // The silhouette at rest over its bounding rectangle: the layout spaces by the rectangle.
        var rest = m.Vertices.Select(v => math.rotate(fits[i].Rest, v)).ToArray();
        var cell = fits[i].Nominal / 12f;
        var cells = fits[i].Fine.Select(p => (int2) math.floor(math.rotate(fits[i].Rest, p).xz / cell)).Distinct().Count();
        var rect = new StoneShape(rest).Footprint(0f, out _);
        var fill = Environment.GetEnvironmentVariable("BWT_FIT_FILL") is null ? 1f : math.min(1f, cells * cell * cell / (rect.x * rect.y));
        fits[i].Fill = fill;
        var squeeze = math.sqrt(fill);

        return new StoneShape([.. rest.Select(v => new float3(v.x * squeeze, v.y, v.z * squeeze))]);
      }).ToList();
      var solids = meshes.Select(m => HeapMetrics.Solid(m, voxel)).ToList();

      foreach (var site in onlySites) {
        var (outline, ground) = FitSpikeRun.Site(site, s);
        var plain = BreakwaterLayout.Lay(outline, shapes, FitSpikeRun.Dumped, new FixtureHost(ground));
        var layout = BreakwaterLayout.Lay(outline, restShapes, FitSpikeRun.Dumped with {
          Embed = 0f,
          Density = float.Parse(Environment.GetEnvironmentVariable("BWT_FIT_DENSITY") ?? "1", CultureInfo.InvariantCulture)
        }, new FixtureHost(ground));

        foreach (var (variant, options) in FitSpikeRun.Variants()) {
          var watch = Stopwatch.StartNew();
          SettleFit.Evaluations = 0;

          var stones = options is null
            ? plain.Stones.ToList()
            : SettleFit.Fit(layout.Stones, fits, ground, options, FitSpikeRun.Dumped.Seed);

          watch.Stop();

          var name = $"{set}-{site}-{variant}";
          FitSpikeRun.WriteRun(Path.Combine(out_, name + ".json"), site, s, outline, stones, meshes);

          var metrics = HeapMetrics.Measure(stones, solids, voxel, outline, ground, s);

          report.AppendLine(
            $"{name}: {metrics} | fill {fits.Average(f => f.Fill):F2} {stones.Count} units, {watch.Elapsed.TotalMilliseconds:F1} ms, " +
            $"{SettleFit.Evaluations / (double) math.max(1, stones.Count):F0} evals/unit, " +
            $"fine {fits.Average(f => f.Fine.Length):F0}"
          );
        }
      }
    }

    File.WriteAllText(Path.Combine(out_, "report.txt"), report.ToString());
  }

  internal static void WriteObj(string path, SpikeMesh mesh) {
    var sb = new StringBuilder();

    foreach (var v in mesh.Vertices) {
      sb.Append(CultureInfo.InvariantCulture, $"v {v.x} {v.y} {v.z}\n");
    }

    foreach (var t in mesh.Triangles) {
      sb.Append(CultureInfo.InvariantCulture, $"f {t.x + 1} {t.y + 1} {t.z + 1}\n");
    }

    File.WriteAllText(path, sb.ToString());
  }

  internal static void WriteRun(
    string path,
    string site,
    float s,
    float2[] outline,
    IReadOnlyList<LaidStone> stones,
    SpikeMesh[] meshes
  ) {
    var sb = new StringBuilder();
    sb.Append(CultureInfo.InvariantCulture, $"{{\"site\":\"{site}\",\"s\":{s},\"outline\":[");
    sb.Append(string.Join(",", outline.Select(p => string.Create(CultureInfo.InvariantCulture, $"[{p.x},{p.y}]"))));
    sb.Append("],\"units\":[");
    sb.Append(
      string.Join(
        ",\n",
        stones.Select(st => string.Create(
            CultureInfo.InvariantCulture,
            $"[\"{meshes[st.Shape].Name}\",{st.Position.x},{st.Position.y},{st.Position.z},{st.Rotation.value.x},{st.Rotation.value.y},{st.Rotation.value.z},{st.Rotation.value.w}]"
          )
        )
      )
    );
    sb.Append("]}");
    File.WriteAllText(path, sb.ToString());
  }
}
