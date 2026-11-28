// SPIKE (ticket 04), throwaway: the port's heaped lay, its height and cover by density and embed.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BreakwaterTool.Layout;
using BreakwaterTool.Tests.Layout;
using Unity.Mathematics;
using Xunit;

namespace BreakwaterTool.Tests.FitSpike;

public sealed class PortRun {
  [Fact]
  public void Run() {
    Assert.SkipUnless(Environment.GetEnvironmentVariable("BWT_PORT_SPIKE") is not null, "spike");

    var sets = Environment.GetEnvironmentVariable("BWT_FIT_SETS")?.Split(',') ?? ["boulders"];
    var densities = (Environment.GetEnvironmentVariable("BWT_DENSITIES") ?? "1.9,1.5,1.2,1")
      .Split(',')
      .Select(static d => float.Parse(d, CultureInfo.InvariantCulture));
    var embeds = (Environment.GetEnvironmentVariable("BWT_EMBEDS") ?? "0.5,0.15")
      .Split(',')
      .Select(static d => float.Parse(d, CultureInfo.InvariantCulture))
      .ToArray();
    var report = new StringBuilder();

    foreach (var set in sets) {
      var meshes = FitSpikeRun.Sets()[set];
      var shapes = meshes
        .Select(static m => new StoneShape(m.Vertices, [.. m.Triangles.SelectMany(static t => new[] { t.x, t.y, t.z })]))
        .ToList();
      var s = shapes.Average(static shape => shape.Heap.Size);
      var nominal = shapes.Average(static shape => shape.Heap.Nominal);
      var voxel = nominal / 10f;
      var solids = meshes.Select(m => HeapMetrics.Solid(m, voxel)).ToList();

      foreach (var site in new[] { "slope", "jetty" }) {
        var (outline, ground) = FitSpikeRun.Site(site, s);

        foreach (var density in densities) {
          foreach (var embed in embeds) {
            foreach (var isFitting in new[] { false, true }) {
              BreakwaterLayout.DebugIsFitting = isFitting;

              var layout = BreakwaterLayout.Lay(
                outline,
                shapes,
                FitSpikeRun.Dumped with { Density = density, Embed = embed },
                new FixtureHost(ground)
              );

              BreakwaterLayout.DebugIsFitting = true;

              var stones = layout.Stones;
              var metrics = HeapMetrics.Measure(stones, solids, voxel, outline, ground, nominal);

              // How high the heap stands over the ground, in nominal sizes: its highest stone top.
              var top = stones.Max(stone => solids[stone.Shape]
                .Max(p => {
                  var w = stone.Position + math.rotate(stone.Rotation, p);

                  return w.y - ground(w.xz);
                })
              ) / nominal;

              var name = $"{set}-{site}-d{density:F1}-e{embed:F2}-{(isFitting ? "fitted" : "tumble")}";

              FitSpikeRun.WriteRun(
                Path.Combine(FitSpikeRun.OutDir, name + ".json"),
                site,
                s,
                outline,
                stones,
                meshes
              );

              report.AppendLine(
                CultureInfo.InvariantCulture,
                $"{name}: {stones.Count} units, {metrics} (thick in Dn), top {top:F1} Dn"
              );
            }
          }
        }
      }
    }

    File.WriteAllText(Path.Combine(FitSpikeRun.OutDir, "port-report.txt"), report.ToString());
  }
}
