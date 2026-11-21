using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BreakwaterTool.Breakwaters;
using BreakwaterTool.Layout;
using BreakwaterTool.Scenarios.Ground;
using Colossal;
using Colossal.IO.AssetDatabase;
using Colossal.Serialization.Entities;
using Colossal.TestFramework;
using Game;
using Game.Assets;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.UI;
using Game.UI.Editor;
using Game.UI.Menu;
using JetBrains.Annotations;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BreakwaterTool.Scenarios;

/// <summary>
/// Loads the proving ground under a new map in the editor and lays its sites through
/// <see cref="BreakwaterApi"/>, as another mod would.
/// The tests run in the order they are declared, on the one map, each from the city the one before
/// left: after each step, the city must hold exactly the stones each breakwater is expected to.
/// Anarchy's editor option starts off, its default, which makes the first lay the control showing
/// what the game does to plain props, then goes on; it changes in memory only, and is set back at
/// the end.
/// </summary>
[TestDescriptor($"{nameof(BreakwaterTool)}: Proving ground", ScenarioRegistry.ScenarioCategory)]
[UsedImplicitly]
internal sealed class ProvingGroundScenario : ModTestScenario {
  /// <summary>
  /// The site laid with the option off, then on, under which the ground is raised.
  /// </summary>
  private const string ControlSite = "level";

  /// <summary>
  /// How far a fixture's number may be from the game's, in metres: the table holds millimetres, and
  /// a size or a centre comes from two of them.
  /// </summary>
  private const float TableRounding = 0.0015f;

  /// <summary>
  /// The name of the map and the city the scenario saves, which it deletes before and after.
  /// </summary>
  private const string SaveName = "BreakwaterTool proving ground";

  /// <summary>
  /// A vanilla prop the game neither hides nor re-seats.
  /// </summary>
  private const string InertProp = "ShipwayBuoy01";

  /// <summary>
  /// A vanilla prop the game re-seats, which Anarchy cannot protect, as it is not overridable.
  /// </summary>
  private const string UnprotectableProp = "BicycleStop03";

  /// <summary>
  /// The name of the prefab the save story removes between a save and its load.
  /// </summary>
  private const string GoneStone = "BreakwaterTool gone boulder";

  // The stones each breakwater is expected to hold, by record, kept from step to step.
  private readonly Dictionary<Entity, Stone[]> expected = [];

  // Each laid site's breakwater, by the site's name.
  private readonly Dictionary<string, Entity> records = [];

  // Set once the ground is loaded, for the tests after the first.
  private bool isGroundLoaded;

  // The user's Anarchy editor option, set back at the end; null until the scenario changes it.
  private bool? userEditorOption;

  // The user's last save, the main menu's Continue, which the scenario's own saves take.
  private SaveGameMetadata? userLastSave;

  private static ProvingSite Control => ProvingSites.Named(ProvingGroundScenario.ControlSite);

  /// <summary>
  /// Where the option's control lays: the control site's outline moved inland, past every plot,
  /// onto plain ground, which leaves the site itself as its picture shows it.
  /// </summary>
  private static float2[] ControlOutline => ProvingGroundScenario.Inland(1200f);

  /// <summary>
  /// Where the save story lays the breakwater of the prefab it removes.
  /// </summary>
  private static float2[] GoneOutline => ProvingGroundScenario.Inland(1600f);

  protected override Task OnPrepare() {
    this.userEditorOption = BreakwaterSystem.DebugEditorOption;
    this.userLastSave = GameManager.instance.settings.userState.lastSaveGameMetadata;
    BreakwaterSystem.DebugEditorOption = false;

    // Every editor-option test rests on setting the option: one that cannot be set fails here, by
    // the lookup to change, and not later on hidden stones.
    if (BreakwaterSystem.DebugEditorOption is not false) {
      throw new TestScenarioException(
        "Anarchy's editor option cannot be set off: check the lookup of " +
        "AnarchyMod.Instance.Settings.PreventOverrideInEditor in AnarchyBridgeLink."
      );
    }

    ProvingGroundScenario.DeleteSaves();

    return Task.CompletedTask;
  }

  protected override async Task OnCleanup() {
    if (this.userEditorOption is {} option) {
      BreakwaterSystem.DebugEditorOption = option;
    }

    var userState = GameManager.instance.settings.userState;

    userState.lastSaveGameMetadata = this.userLastSave;
    userState.ApplyAndSave();

    await base.OnCleanup();

    // The saves go once the main menu has let go of the city loaded from them.
    ProvingGroundScenario.DeleteSaves();
  }

  [Test]
  [UsedImplicitly]
  private async Task Ground_LoadsAsBuilt() {
    await this.EnsureGround();

    this.Check(
      ProvingGroundLoader.IsLoaded(this.World),
      "The game's heights are the proving ground's, value for value."
    );

    this.Check(
      ProvingGroundLoader.IsMapped(this.World),
      "The game maps the world onto them as the off-engine fixtures do: same resolution, scale " +
      "and offset."
    );

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task Stones_AreTheFixtures() {
    await this.EnsureGround();

    (string Choice, IReadOnlyList<StoneShape> Fixtures)[] choices = [
      ("BoulderRandom01", FixtureStones.Boulders), ("CratePile01", FixtureStones.Crates)
    ];

    foreach (var (choice, fixtures) in choices) {
      var stones = BreakwaterSystem.DebugShapes(choice)
        .OrderBy(static stone => stone.Name)
        .ToArray();

      this.Check(
        stones.Length == fixtures.Count,
        $"{choice} lays {stones.Length} stones, the fixtures stand for {fixtures.Count}."
      );

      foreach (var ((name, shape), fixture) in stones.Zip(fixtures, static (a, b) => (a, b))) {
        var game = ProvingGroundScenario.Describe(shape);
        var isAgreed = ProvingGroundScenario.Facts(shape)
          .Zip(ProvingGroundScenario.Facts(fixture), static (a, b) => math.abs(a - b))
          .All(static gap => gap <= ProvingGroundScenario.TableRounding);

        this.Check(
          isAgreed,
          isAgreed
            ? $"{name}: {game}."
            : $"{name}: {game}, its fixture {ProvingGroundScenario.Describe(fixture)}: " +
            "measure the stones again, then approve the pictures again."
        );
      }
    }

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task EditorOptionOff_TheGameHidesAndMovesTheStones() {
    await this.EnsureGround();

    this.Check(
      BreakwaterSystem.DebugProtection()
        .EndsWith($"editor option {false}", StringComparison.Ordinal),
      $"The tool reads Anarchy's editor option as off: {BreakwaterSystem.DebugProtection()}."
    );

    var site = ProvingGroundScenario.Control;
    var outline = ProvingGroundScenario.ControlOutline;

    var record = await this.Lay(
      "control",
      outline,
      ProvingGroundScenario.Prefabs(site.Stones),
      site.Parameters
    );

    // On the same outline, a prop the game neither hides nor re-seats, which needs no protection.
    var inert = await this.Lay(
      ProvingGroundScenario.InertProp,
      outline,
      BreakwaterSystem.DebugPrefabs(ProvingGroundScenario.InertProp),
      site.Parameters
    );

    var laid = this.expected[record].Length;
    var hidden = CityStones.Hidden(this.World, record);

    this.Check(
      hidden > 0,
      $"The game hides {hidden} of the {laid} stones it finds overlapping. None: Anarchy now " +
      "protects in the editor by default, so drop the editor option warning, and find another " +
      "control."
    );

    this.Check(
      this.expected[inert].Length > 0 && CityStones.Hidden(this.World, inert) == 0,
      $"Beside it, the game hides none of the {this.expected[inert].Length} " +
      $"{ProvingGroundScenario.InertProp} stones."
    );

    var rise = await this.Raise(outline);
    var moved = this.City(record);
    var inertMoved = this.City(inert);

    this.Check(
      rise > 1f && moved.Missing.Count > 0,
      $"Raised {rise:F1} m, the ground moves {moved.Missing.Count} of the {laid} stones: {moved}."
    );

    this.Check(
      inertMoved.Holds,
      $"Raised, the ground moves none of the {ProvingGroundScenario.InertProp} stones: " +
      $"{inertMoved}."
    );

    this.Check(
      BreakwaterApi.Delete(record) is null && BreakwaterApi.Delete(inert) is null,
      "The control and its inert neighbour are deleted."
    );

    await this.WaitFrames(2);

    this.expected.Remove(record);
    this.expected.Remove(inert);
    this.records.Remove("control");
    this.records.Remove(ProvingGroundScenario.InertProp);
    this.CheckCity("Deleted, the control and its neighbour leave no stone");

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task Sites_LayAsPlanned() {
    await this.EnsureGround();

    BreakwaterSystem.DebugEditorOption = true;

    foreach (var (site, index) in ProvingSites.All.Select(static (site, i) => (site, i + 1))) {
      this.Step(site.Name, index, ProvingSites.All.Count);

      if (site.Verdict is LayoutVerdict.Laid) {
        await this.Lay(site);
        this.CheckCity(site.Name);

        continue;
      }

      var outcome = BreakwaterApi.Create(
        site.Outline,
        ProvingGroundScenario.Prefabs(site.Stones),
        site.Parameters
      );

      this.Check(
        outcome.Breakwater == Entity.Null && outcome.WhyNothing is not null,
        $"{site.Name} lays nothing: {outcome.WhyNothing}."
      );
    }

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task Album_ShowsEachSite() {
    await this.EnsureGround();

    var water = this.World.GetExistingSystemManaged<WaterSystem>();
    var planetary = this.World.GetExistingSystemManaged<PlanetarySystem>();
    var (isTimeOverridden, time) = (planetary.overrideTime, planetary.time);

    // The editor's sea level, applied as its water panel applies it, and a morning sun, its
    // shadows across the rows.
    water.SeaLevel = (float) ProvingGround.SeaLevel;
    water.ResetToSealevel();
    planetary.overrideTime = true;
    planetary.time = 10f;

    // The sea spreads in from the map's edges over many frames: wait for it out at sea in the map's
    // middle, the last place it reaches.
    var openSea = new float2(0f, -1000f);

    await this.WaitFrames(120);

    for (var frame = 0; frame < 2400 && Album.SeaDepth(this.World, openSea) < 5f; frame += 30) {
      await this.WaitFrames();
    }

    this.Check(
      Album.SeaDepth(this.World, openSea) >= 5f,
      $"The sea fills the map: {Album.SeaDepth(this.World, openSea):F1} m deep out at sea in its " +
      "middle."
    );

    var album = new Album();
    var sites = ProvingSites.All.Where(static site => site.Verdict is LayoutVerdict.Laid).ToArray();

    foreach (var (site, index) in sites.Select(static (site, i) => (site, i + 1))) {
      this.Step(site.Name, index, sites.Length);
      Album.Aim(this.World, site);

      await this.WaitFrames();

      album.Shoot(site);
    }

    var page = album.Close();

    planetary.overrideTime = isTimeOverridden;
    planetary.time = time;

    this.Check(
      sites.All(site => File.Exists(Path.Combine(album.Folder, $"{site.Name}.png"))),
      $"The album shows the {sites.Length} sites: {page}."
    );

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task EditorOptionOn_TheStonesHoldThroughATerrainEdit() {
    await this.EnsureGround();

    var site = ProvingGroundScenario.Control;
    var rise = await this.Raise(site.Outline);

    this.Check(rise > 1f, $"The ground under {site.Name} rises {rise:F1} m.");
    this.CheckCity($"Raised under {site.Name}");

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task Operations_EachLeavesTheCityAsPlanned() {
    await this.EnsureGround();

    var listed = BreakwaterApi.List();

    this.Check(
      listed.Length == this.expected.Count && listed.All(this.expected.ContainsKey),
      $"List gives the {listed.Length} breakwaters laid, of {this.expected.Count}."
    );

    var jettySite = ProvingSites.Named("jetty");
    var jetty = this.records[jettySite.Name];
    var info = BreakwaterApi.Read(jetty);

    this.Check(
      info is not null &&
      info.Polygon.SequenceEqual(jettySite.Outline) &&
      info.Parameters == jettySite.Parameters &&
      info.Stones.Length == this.expected[jetty].Length,
      $"Read gives the jetty as laid: {info?.Polygon.Length} points, {info?.Stones.Length} stones."
    );

    // A stone moved by hand before the pick: only the pick's own stones hold it after the revert,
    // and no fresh lay of the jetty's parameters does.
    var manager = this.World.EntityManager;
    var nudgedStone = BreakwaterApi.Read(jetty)!.Stones[0];
    var nudged = manager.GetComponentData<Transform>(nudgedStone);

    nudged.m_Position.x += 1f;
    manager.SetComponentData(nudgedStone, nudged);

    var picked = CityStones.ByRecord(this.World)[jetty];

    this.Check(
      BreakwaterSystem.DebugPick(jetty).StartsWith("picked", StringComparison.Ordinal),
      "The jetty is picked, a stone of it moved by hand."
    );

    this.Check(
      BreakwaterApi.Edit(
        jetty,
        parameters: jettySite.Parameters with {
          Seed = 2
        }
      ) is null,
      "The jetty is edited to another seed."
    );

    await this.WaitFrames(2);

    this.Expect(jetty);
    this.CheckCity("Edited to another seed, the jetty");
    this.Check(BreakwaterSystem.DebugRevert() is null, "The jetty is reverted to its pick.");

    await this.WaitFrames(2);

    this.expected[jetty] = picked;
    this.CheckCity("Reverted to its pick, the jetty has its stone moved by hand");

    this.Check(
      BreakwaterApi.Read(jetty)?.Parameters == jettySite.Parameters,
      "Reverted, the jetty reads its parameters as picked."
    );

    // A player's hand: a whole breakwater bulldozed stone by stone, as the bulldozer deletes, and
    // a stone moved a metre, as Move It moves one.
    var revetment = this.records["revetment"];
    var bulldozed = BreakwaterApi.Read(revetment)!.Stones;
    var stones = BreakwaterApi.Read(jetty)!.Stones;
    var moved = manager.GetComponentData<Transform>(stones[0]);

    moved.m_Position.x += 1f;
    manager.SetComponentData(stones[0], moved);

    foreach (var stone in bulldozed) {
      manager.AddComponent<Deleted>(stone);
    }

    await this.WaitFrames(2);

    var broken = this.City();

    this.Check(
      broken.Missing.Count == bulldozed.Length + 1 && broken.Extra.Count == 1,
      $"The revetment's {bulldozed.Length} stones deleted and a jetty stone moved by hand: " +
      $"{broken}."
    );

    this.Check(
      BreakwaterApi.FitToTerrain(revetment) is null && BreakwaterApi.FitToTerrain(jetty) is null,
      "The revetment and the jetty are fitted again."
    );

    await this.WaitFrames(2);

    this.Expect(jetty);
    this.CheckCity("Fitted, the revetment has every stone back and the jetty its moved one");

    // Choices the tool leaves out: a prop Anarchy cannot protect, beside the crates, alone, and a
    // prefab that is no stone at all.
    var cornerToeSite = ProvingSites.Named("corner-toe");
    var cornerToe = this.records[cornerToeSite.Name];
    var unprotectable = BreakwaterSystem.DebugPrefabs(ProvingGroundScenario.UnprotectableProp);
    var crates = ProvingGroundScenario.Prefabs(cornerToeSite.Stones);

    this.Check(
      BreakwaterApi.Edit(cornerToe, stonePrefabs: [.. crates, .. unprotectable]) is null &&
      BreakwaterSystem.DebugLastLayout()
        .Contains($"unprotectable [{ProvingGroundScenario.UnprotectableProp}]"),
      $"Choosing {ProvingGroundScenario.UnprotectableProp} beside the crates, the corner-toe " +
      $"site leaves it out: {BreakwaterSystem.DebugLastLayout()}."
    );

    await this.WaitFrames(2);

    this.Expect(cornerToe);
    this.CheckCity(
      $"Edited to choose {ProvingGroundScenario.UnprotectableProp} too, the corner-toe"
    );

    var alone = BreakwaterApi.Create(cornerToeSite.Outline, unprotectable);
    var car = BreakwaterApi.Create(cornerToeSite.Outline, [ProvingGroundScenario.Car(this.World)]);

    this.Check(
      alone.Breakwater == Entity.Null && alone.WhyNothing is not null,
      $"Choosing {ProvingGroundScenario.UnprotectableProp} alone lays nothing: {alone.WhyNothing}."
    );

    this.Check(
      car.Breakwater == Entity.Null && car.WhyNothing is not null,
      $"Choosing a car lays nothing: {car.WhyNothing}."
    );

    var nearGuardSite = ProvingSites.Named("near-guard");

    this.Check(
      BreakwaterApi.Delete(this.records[nearGuardSite.Name]) is null,
      "The near-guard site is deleted."
    );

    await this.WaitFrames(2);

    this.expected.Remove(this.records[nearGuardSite.Name]);

    // Created in the frame's most fragile place, as another mod's system may call.
    var outcome = new BreakwaterOutcome(Entity.Null, "not called");

    await LateCallSystem.Call(() =>
      outcome = BreakwaterApi.Create(
        nearGuardSite.Outline,
        ProvingGroundScenario.Prefabs(nearGuardSite.Stones),
        nearGuardSite.Parameters
      )
    );

    this.Check(
      outcome.WhyNothing is null,
      $"The near-guard site is created again from {nameof(SystemUpdatePhase.ModificationEnd)}: " +
      $"{outcome.WhyNothing ?? "laid"}."
    );

    await this.WaitFrames(2);

    this.Expect(outcome.Breakwater);
    this.records[nearGuardSite.Name] = outcome.Breakwater;
    this.CheckCity(
      $"Created from {nameof(SystemUpdatePhase.ModificationEnd)}, the near-guard site"
    );

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task Saves_KeepEveryStone() {
    await this.EnsureGround();

    this.CheckCity("Before saving");
    this.CheckRecords("Before saving");

    var capture = this.expected.Values.SelectMany(static stones => stones).ToArray();
    var map = await this.SaveMap();

    await this.Load(GameMode.Editor, Purpose.LoadMap, map);
    this.CheckSaved("The saved map, loaded in the editor", capture);
    this.CheckRecords("The saved map, loaded in the editor");

    await this.StartNewCity(map.id, ProvingGroundScenario.SaveName);
    this.CheckSaved("A city started from the map", capture);
    this.CheckRecords("A city started from the map");

    // A breakwater of a prefab the next load will not find, as when its asset or its mod is gone: a
    // boulder's copy under a name of its own, laid inland, saved, then removed with its stones.
    var prefabs = this.World.GetExistingSystemManaged<PrefabSystem>();

    var goneStone = prefabs.DuplicatePrefab(
      prefabs.GetPrefab<StaticObjectPrefab>(BreakwaterSystem.DebugPrefabs("Boulder01")[0]),
      ProvingGroundScenario.GoneStone
    );

    // The game sets a new prefab's data up over its next updates.
    await this.WaitFrames(10);

    var goneOutline = ProvingGroundScenario.GoneOutline;

    var gone = await this.Lay(
      ProvingGroundScenario.GoneStone,
      goneOutline,
      [prefabs.GetEntity(goneStone)],
      BreakwaterParameters.Default
    );

    var city = await this.SaveCity();
    var goneLaid = this.expected[gone].Length;

    this.Check(
      BreakwaterApi.Delete(gone) is null,
      "The stand-in breakwater is saved, then deleted."
    );

    await this.WaitFrames(2);

    this.expected.Remove(gone);
    this.records.Remove(ProvingGroundScenario.GoneStone);
    prefabs.RemovePrefab(goneStone);

    await this.WaitFrames(2);
    await this.Load(GameMode.Game, Purpose.LoadGame, city);

    gone = BreakwaterApi.List()
      .FirstOrDefault(record =>
        BreakwaterApi.Read(record)?.Polygon.SequenceEqual(goneOutline) is true
      );

    this.CheckSaved("The saved city, loaded", capture, gone);
    this.CheckRecords("The saved city, loaded", gone);
    this.CheckGone(gone, goneLaid);

    var why = BreakwaterApi.FitToTerrain(gone);

    await this.WaitFrames(2);

    this.Check(
      why is not null || CityStones.ByRecord(this.World).ContainsKey(gone),
      $"Fitted, the breakwater of the removed prefab answers: {why ?? "laid"}."
    );

    await this.SaveCity();

    this.Check(
      BreakwaterApi.Delete(gone) is null,
      "The city saves again, and the breakwater of the removed prefab is deleted."
    );

    await this.WaitFrames(2);

    // The one fit: the control site, raised and left as laid since the editor, which no load laid
    // again, as the checks above prove.
    this.expected.Clear();

    foreach (var (record, stones) in CityStones.ByRecord(this.World)) {
      this.expected[record] = stones;
    }

    var level = this.expected.Keys.First(static record =>
      BreakwaterApi.Read(record)
        ?.Polygon.SequenceEqual(ProvingGroundScenario.Control.Outline) is true
    );

    var before = this.expected[level];

    this.Check(BreakwaterApi.FitToTerrain(level) is null, "The control site is fitted.");

    await this.WaitFrames(2);

    this.Expect(level);

    var reseated = CityStones.Compare(before, this.expected[level]);

    this.Check(
      reseated.Missing.Count == before.Length,
      $"Fitted, the control site's stones all move to the raised ground: {reseated}."
    );

    this.CheckCity("Fitted in the city, the control site");

    await this.Finish();
  }

  /// <summary>
  /// A shape as the layout sees it, in its long axis frame, to the millimetre the fixture table
  /// holds: its whole footprint, which sets that frame, the footprint above the default embed line,
  /// and its height.
  /// </summary>
  private static string Describe(StoneShape shape) {
    var whole = shape.Footprint(0f, out var wholeCentre);
    var shown = shape.Footprint(BreakwaterParameters.Default.Embed, out var shownCentre);

    return string.Join(
      ", ",
      FormattableString.Invariant(
        $"whole {whole.x:F3} × {whole.y:F3} at ({wholeCentre.x:F3}, {wholeCentre.y:F3})"
      ),
      FormattableString.Invariant(
        $"shown {shown.x:F3} × {shown.y:F3} at ({shownCentre.x:F3}, {shownCentre.y:F3})"
      ),
      FormattableString.Invariant($"{shape.MinY:F3} to {shape.MaxY:F3}")
    );
  }

  /// <summary>
  /// The numbers <see cref="Describe"/> shows.
  /// </summary>
  private static float[] Facts(StoneShape shape) {
    var whole = shape.Footprint(0f, out var wholeCentre);
    var shown = shape.Footprint(BreakwaterParameters.Default.Embed, out var shownCentre);

    return [
      whole.x,
      whole.y,
      wholeCentre.x,
      wholeCentre.y,
      shown.x,
      shown.y,
      shownCentre.x,
      shownCentre.y,
      shape.MinY,
      shape.MaxY
    ];
  }

  /// <summary>
  /// The prefabs that stand in the game for a site's fixture stones.
  /// </summary>
  private static Entity[] Prefabs(SiteStones stones) =>
    BreakwaterSystem.DebugPrefabs(
      stones is SiteStones.Boulders ? "BoulderRandom01" : "CratePile01"
    );

  private static float2[] Inland(float metres) => [
    .. ProvingGroundScenario.Control.Outline.Select(point => point + new float2(0f, metres))
  ];

  /// <summary>
  /// A car's prefab, which has a geometry and is no stone.
  /// </summary>
  private static Entity Car(World world) {
    using var query = world.EntityManager.CreateEntityQuery(
      ComponentType.ReadOnly<CarData>(),
      ComponentType.ReadOnly<PrefabData>()
    );

    using var cars = query.ToEntityArray(Allocator.Temp);

    return cars[0];
  }

  /// <summary>
  /// Lays a site through the API, and expects the game's own plan for it from then on.
  /// </summary>
  private Task<Entity> Lay(ProvingSite site) =>
    this.Lay(site.Name, site.Outline, ProvingGroundScenario.Prefabs(site.Stones), site.Parameters);

  /// <summary>
  /// Lays a breakwater through the API, and expects the game's own plan for it from then on.
  /// </summary>
  private async Task<Entity> Lay(
    string name,
    float2[] outline,
    Entity[] choice,
    BreakwaterParameters parameters
  ) {
    var outcome = BreakwaterApi.Create(outline, choice, parameters);

    if (outcome.Breakwater == Entity.Null) {
      this.Check(false, $"{name} lays nothing: {outcome.WhyNothing}.");
    }

    // The system lays at its next update.
    await this.WaitFrames(2);

    this.Expect(outcome.Breakwater);
    this.records[name] = outcome.Breakwater;

    return outcome.Breakwater;
  }

  /// <summary>
  /// Expects the game's own plan for a breakwater from then on.
  /// </summary>
  private void Expect(Entity record) =>
    this.expected[record] = BreakwaterSystem.DebugPlan(record) is {} plan
      ? CityStones.Named(this.World, plan)
      : [];

  /// <summary>
  /// Raises the ground under an outline's middle by some metres with the terrain tool's brush, and
  /// gives the rise once the game's systems had the frames to follow it.
  /// </summary>
  private async Task<float> Raise(float2[] outline) {
    const float metres = 4f;

    var middle = outline.Aggregate(float2.zero, static (sum, point) => sum + point) /
      outline.Length;

    var before = Terraform.Height(this.World, middle);

    for (var stroke = 0; stroke < 200; stroke++) {
      if (Terraform.Height(this.World, middle) - before >= metres) {
        break;
      }

      Terraform.Stroke(this.World, middle);

      await this.WaitFrames(2);
    }

    await this.WaitFrames(60);

    return Terraform.Height(this.World, middle) - before;
  }

  private StoneCheck City(Entity record) =>
    CityStones.Compare(
      this.expected[record],
      CityStones.ByRecord(this.World).TryGetValue(record, out var stones) ? stones : []
    );

  private StoneCheck City() =>
    CityStones.Compare(
      this.expected.Values.SelectMany(static stones => stones),
      CityStones.ByRecord(this.World).Values.SelectMany(static stones => stones)
    );

  /// <summary>
  /// Checks that the city holds exactly the stones expected, all of them visible.
  /// </summary>
  private void CheckCity(string step) {
    var city = this.City();
    var hidden = CityStones.Hidden(this.World);

    this.Check(city.Holds && hidden == 0, $"{step}: {city}, {hidden} hidden.");
  }

  /// <summary>
  /// Checks that a loaded map or city holds exactly the stones captured before it was saved, all of
  /// them visible, but for one breakwater's when the step names it.
  /// </summary>
  private void CheckSaved(string step, IEnumerable<Stone> capture, Entity except = default) {
    var city = CityStones.Compare(
      capture,
      CityStones.ByRecord(this.World)
        .Where(pair => pair.Key != except)
        .SelectMany(static pair => pair.Value)
    );

    var hidden = CityStones.Hidden(this.World);

    this.Check(city.Holds && hidden == 0, $"{step}: {city}, {hidden} hidden.");
  }

  /// <summary>
  /// Checks that the breakwaters the API lists are those the stones link to, each choosing prefabs
  /// the game has, but for one breakwater when the step names it.
  /// </summary>
  private void CheckRecords(string step, Entity except = default) {
    var manager = this.World.EntityManager;
    var prefabs = this.World.GetExistingSystemManaged<PrefabSystem>();
    var listed = BreakwaterApi.List().Where(record => record != except).ToArray();
    var linked = CityStones.ByRecord(this.World).Keys.Where(record => record != except).ToArray();

    var choices = listed
      .SelectMany(static record => BreakwaterApi.Read(record)?.StonePrefabs ?? [])
      .Distinct()
      .Select(prefab => manager.Exists(prefab) && manager.HasComponent<PrefabData>(prefab)
        ? prefabs.GetPrefabName(prefab)
        : $"{prefab}, gone"
      )
      .ToArray();

    this.Check(
      listed.Length == linked.Length &&
      listed.All(linked.Contains) &&
      !choices.Any(static choice => choice.EndsWith(", gone", StringComparison.Ordinal)),
      $"{step}: {listed.Length} breakwaters listed, {linked.Length} linked to, choosing " +
      $"{string.Join(", ", choices)}."
    );
  }

  /// <summary>
  /// Checks that the breakwater of a removed prefab loads with its choice read as gone, and every
  /// stone it laid, which the game keeps on its placeholder of the prefab.
  /// </summary>
  private void CheckGone(Entity gone, int laid) {
    var manager = this.World.EntityManager;
    var choice = gone == Entity.Null ? [] : BreakwaterApi.Read(gone)!.StonePrefabs;
    var stones = CityStones.ByRecord(this.World).TryGetValue(gone, out var linked) ? linked : [];

    var isGone = choice.Length > 0 &&
      choice.All(prefab =>
        !manager.Exists(prefab) ||
        !manager.HasComponent<PrefabData>(prefab) ||
        !manager.IsComponentEnabled<PrefabData>(prefab)
      );

    this.Check(
      gone != Entity.Null && isGone && stones.Length == laid,
      $"The breakwater of the removed prefab loads, choosing {string.Join(", ", choice)}, read " +
      $"as gone: {isGone}; {stones.Length} of its {laid} stones loaded, as " +
      $"{string.Join(", ", stones.Select(static stone => stone.Prefab).Distinct())}."
    );
  }

  /// <summary>
  /// Saves the editor's map as its map panel saves a new one.
  /// </summary>
  private async Task<MapMetadata> SaveMap() {
    var saved = new TaskCompletionSource<MapMetadata>();

    typeof(MapPanelSystem)
      .GetMethod("OnSaveMap", BindingFlags.NonPublic | BindingFlags.Instance)!
      .Invoke(
        this.World.GetExistingSystemManaged<MapPanelSystem>(),
        [ProvingGroundScenario.SaveName, null, new Action<MapMetadata>(saved.SetResult)]
      );

    return await this.Within(saved.Task, "The map is not saved.");
  }

  /// <summary>
  /// Saves the city as the game's exit backup does, with no preview.
  /// </summary>
  private async Task<SaveGameMetadata> SaveCity() {
    var saved = new TaskCompletionSource<bool>();
    var info = this.World.GetExistingSystemManaged<MenuUISystem>().GetSaveInfo(false);

    // Awaited through the completion source, which the save's own task sets.
    _ = TaskManager.instance.EnqueueTask(
      "SaveLoadGame",
      async () => saved.SetResult(
        await GameManager.instance.Save(
          ProvingGroundScenario.SaveName,
          info,
          AssetDatabase.user,
          (ScreenCaptureHelper.AsyncRequest?) null
        )
      ),
      1
    );

    await this.Within(saved.Task, "The city is not saved.");

    return AssetDatabase.user.GetAssets(default(SearchFilter<SaveGameMetadata>))
        .FirstOrDefault(static save => save.name == ProvingGroundScenario.SaveName) ??
      throw new TestScenarioException("The saved city is not found.");
  }

  /// <summary>
  /// Deletes the map and the city the scenario saves, as the game deletes a save it overwrites.
  /// </summary>
  private static void DeleteSaves() {
    foreach (var path in (AssetDataPath[]) [
        SaveHelpers.GetAssetDataPath<MapMetadata>(
          AssetDatabase.user,
          ProvingGroundScenario.SaveName
        ),
        SaveHelpers.GetAssetDataPath<SaveGameMetadata>(
          AssetDatabase.user,
          ProvingGroundScenario.SaveName
        )
      ]) {
      if (AssetDatabase.user.Exists<PackageAsset>(path, out var package)) {
        AssetDatabase.user.DeleteAsset(package);
      }
    }
  }

  private async Task Load(GameMode mode, Purpose purpose, IAssetData asset) {
    // The load must start from the game's own loop.
    await this.WaitFrames(2);

    if (!await GameManager.instance.Load(mode, purpose, asset)) {
      throw new TestScenarioException($"{asset} did not load.");
    }

    await this.WaitFrames(60);
  }

  /// <summary>
  /// A task's result, or the test's end when it takes over a minute of the capped frame rate.
  /// </summary>
  private async Task<T> Within<T>(Task<T> task, string failure) =>
    await Task.WhenAny(task, this.WaitFrames(600)) == task
      ? await task
      : throw new TestScenarioException(failure);

  /// <summary>
  /// Loads a new map in the editor with the proving ground under it, unless an earlier test did.
  /// </summary>
  private async Task EnsureGround() {
    if (GameManager.instance.gameMode is GameMode.Editor && this.isGroundLoaded) {
      return;
    }

    // The load must start from the game's own loop.
    await this.WaitFrames(2);

    if (!await GameManager.instance.Load(GameMode.Editor, Purpose.NewMap)) {
      throw new TestScenarioException("The new map did not load.");
    }

    await this.WaitFrames(60);

    ProvingGroundLoader.Apply(this.World);

    await this.WaitFrames(60);

    this.isGroundLoaded = true;
  }
}
