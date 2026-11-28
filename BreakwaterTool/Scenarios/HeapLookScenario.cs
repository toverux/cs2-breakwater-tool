// SPIKE (ticket 04), throwaway: fitted and tumbled heaps side by side, for the by-eye judgment.
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BreakwaterTool.Breakwaters;
using BreakwaterTool.Layout;
using BreakwaterTool.Scenarios.Ground;
using Colossal.Serialization.Entities;
using Colossal.TestFramework;
using Game;
using Game.SceneFlow;
using Game.Rendering;
using Game.Simulation;
using JetBrains.Annotations;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace BreakwaterTool.Scenarios;

// Its own category bit, so a run filtered on it runs this scenario alone.
[TestDescriptor($"{nameof(BreakwaterTool)}: Heap look", (Colossal.TestFramework.Category) (1 << 21))]
[UsedImplicitly]
internal sealed class HeapLookScenario : ModTestScenario {
  private static readonly (string Name, string Stones)[] Sets = [
    ("boulders", "BoulderRandom01"),
    ("rocks", "RockRandom01"),
    ("logs", "LogRandom01"),
    ("standing", "StandingStoneRandom01"),
    ("tetrapod", "Tetrapod"),
    ("tetrapod-small", "Tetrapod one piece - small"),
    ("korean-10m", "CS Korean Style Sea Tetrapod 10m A"),
    ("crates", "CrateRandom01"),
    ("barrels", "BarrelRandom01"),
    ("heads", "StoneHeadRandom01")
  ];

  private static readonly string[] SiteNames = ["revetment", "jetty"];

  [Test]
  [UsedImplicitly]
  private async Task Heaps_FittedBesideTumbled() {
    await this.WaitFrames(2);

    if (!await GameManager.instance.Load(GameMode.Editor, Purpose.NewMap)) {
      throw new TestScenarioException("The new map did not load.");
    }

    await this.WaitFrames(60);
    ProvingGroundLoader.Apply(this.World);
    await this.WaitFrames(60);

    var water = this.World.GetExistingSystemManaged<WaterSystem>();
    var planetary = this.World.GetExistingSystemManaged<PlanetarySystem>();

    water.SeaLevel = (float) ProvingGround.SeaLevel;
    water.ResetToSealevel();
    planetary.overrideTime = true;
    planetary.time = 10f;

    var openSea = new float2(0f, -1000f);

    await this.WaitFrames(120);

    for (var frame = 0; frame < 2400 && Album.SeaDepth(this.World, openSea) < 5f; frame += 30) {
      await this.WaitFrames();
    }

    var album = new Album();
    var rubble = BreakwaterStyles.Find("Rubble")!.Parameters;

    // A fitted heap's stones take room the tumble's share: one layer at about this density.
    var heaped = rubble with { Density = 1.1f, Embed = 0.25f };
    var sites = HeapLookScenario.SiteNames
      .Select(static name => ProvingSites.All.First(site => site.Name == name))
      .ToArray();

    var step = 0;
    var total = HeapLookScenario.Sets.Length * sites.Length;

    foreach (var (setName, stones) in HeapLookScenario.Sets) {
      var choice = BreakwaterSystem.DebugPrefabs(stones)
        .Where(static prefab => prefab != Entity.Null)
        .ToArray();

      if (choice.Length == 0) {
        this.Note(album, $"{setName}: no prefab named {stones}");

        continue;
      }

      foreach (var site in sites) {
        this.Step($"{setName} on {site.Name}", ++step, total);
        BreakwaterLayout.DebugIsFitting = true;

        var outcome = BreakwaterApi.Create(site.Outline, choice, heaped);

        if (outcome.Breakwater == Entity.Null) {
          this.Note(album, $"{setName} on {site.Name}: nothing, {outcome.WhyNothing}");

          continue;
        }

        var watch = Stopwatch.StartNew();

        await this.WaitFrames(2);

        var fitted = $"{BreakwaterApi.Read(outcome.Breakwater)?.Stones.Length} stones in about " +
          $"{watch.Elapsed.TotalMilliseconds:F0} ms";

        await this.Shoot(album, site, $"{site.Name}-{setName}-fitted");

        BreakwaterLayout.DebugIsFitting = false;

        watch.Restart();
        BreakwaterApi.Edit(outcome.Breakwater, parameters: rubble);
        await this.WaitFrames(2);

        var tumbled = $"{BreakwaterApi.Read(outcome.Breakwater)?.Stones.Length} stones in about " +
          $"{watch.Elapsed.TotalMilliseconds:F0} ms";

        await this.Shoot(album, site, $"{site.Name}-{setName}-tumble");

        this.Note(album, $"{setName} on {site.Name}: fitted {fitted}, tumbled {tumbled}");

        BreakwaterLayout.DebugIsFitting = true;
        BreakwaterApi.Delete(outcome.Breakwater);
        await this.WaitFrames(3);
      }
    }

    album.Close();
    this.Check(true, $"Album in {album.Folder}.");

    await this.Finish();
  }

  /// <summary>
  /// A line of the report, written as it comes, and logged so the runner sees the run go on.
  /// </summary>
  private void Note(Album album, string line) {
    File.AppendAllText(Path.Combine(album.Folder, "report.txt"), line + "\n");
    TestScenario.log.Info($"  {line}");
  }

  /// <summary>
  /// The site whole, then up close from aside, low enough to read how the stones rest.
  /// </summary>
  private async Task Shoot(Album album, ProvingSite site, string name) {
    var camera = this.World.GetExistingSystemManaged<CameraUpdateSystem>().activeCameraController;

    Album.Aim(this.World, site);
    await this.WaitFrames(20);
    album.Shoot(site, name);

    camera.zoom = 30f;
    camera.rotation = new Vector3(28f, 25f, 0f);
    await this.WaitFrames(20);
    album.Shoot(site, $"{name}-close");
  }
}
