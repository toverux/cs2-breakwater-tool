using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Colossal;
using Colossal.Reflection;
using Colossal.Serialization.Entities;
using Colossal.TestFramework;
using Game;
using Game.City;
using Game.SceneFlow;
using Game.Simulation;
using JetBrains.Annotations;

namespace BreakwaterTool.Scenarios;

/// <summary>
/// Base class of the mod's in-game test scenarios.
/// A scenario fails on any error logged while it runs, by any logger, as the game's runner rules.
/// The runner also aborts a scenario, before it runs a single test and while still reporting it a
/// success, when an error was logged since the previous one ended: at boot, any of the game's or
/// another mod's.
/// The runner constructs a scenario right before it runs it, so the constructor dismisses those
/// earlier errors, and logs how many it dismissed.
/// The cs2-dev <c>test</c> tool still fails the run on an error of the mod's, in its own log or in
/// another log that names it.
/// </summary>
internal abstract class ModTestScenario : TestScenario {
  /// <summary>
  /// The Archipelago map, which the base game ships.
  /// </summary>
  protected static readonly Hash128 Archipelago = Hash128.Parse("bf8036b291428535b986c757fda3e627");

  // The tests in the order the runner runs them, for the progress overlay.
  private readonly string[] tests;

  // Shown from the first test on, not from the constructor, which may run without a run.
  private ProgressOverlay? overlay;

  // Counted by Check, reset before each test by ResetChecks.
  private int failedChecks;

  // The test running, from 1, moved on by ResetChecks.
  private int testIndex;

  protected ModTestScenario() {
    // The runner lists its tests the same way.
    this.tests = [
      .. this.GetType()
        .GetMethodsWithAttributeAndSignature<TestAttribute>(typeof(void), typeof(Task))
        .Select(static method => ModTestScenario.Words(method.Name))
    ];

    var dismissed = TestScenarioSystem.ConsumeErrorCount();

    if (dismissed > 0) {
      TestScenario.log.Warn(
        $"Dismissed {dismissed} errors logged before the scenario started, by the game or a " +
        "mod: see Player.log and the mods' logs."
      );
    }
  }

  /// <summary>
  /// Returns to the main menu after a scenario that loaded a city or a map.
  /// </summary>
  protected override async Task OnCleanup() {
    this.overlay?.End();

    if (GameManager.instance.gameMode.IsGameOrEditor()) {
      await GameManager.instance.MainMenu();
    }
  }

  /// <summary>
  /// Starts a new city on a map, with the options a new game takes from the new game screen, as the
  /// game's own scenarios set them, and returns once its load has ended.
  /// The city starts in <paramref name="startingYear"/>, the current year by default.
  /// </summary>
  protected async Task StartNewCity(Hash128 map, string cityName, int? startingYear = null) {
    // The load must start from the game's own loop: a load started from the caller of the scenario,
    // the unity eval for one, holds the main thread the load needs.
    await this.WaitFrames(2);

    // The new game screen sets the starting year, which a scenario's load does not: left at zero,
    // reading the city's date throws, for the game and every mod reading it.
    this.World.GetExistingSystemManaged<TimeSystem>().startingYear =
      startingYear ?? DateTime.Now.Year;

    var cityConfiguration = this.World.GetExistingSystemManaged<CityConfigurationSystem>();

    cityConfiguration.overrideLoadedOptions = true;
    cityConfiguration.overrideCityName = cityName;
    cityConfiguration.overrideThemeName = "European";
    cityConfiguration.overrideLeftHandTraffic = false;

    if (!await GameManager.instance.Load(GameMode.Game, Purpose.NewGame, map)) {
      throw new TestScenarioException($"The new city on map {map} did not load.");
    }

    // The load ends a few frames after it returns: the overlay goes, and the simulation takes its
    // speed from the player's paused-after-loading setting, over any speed set before.
    await this.WaitFrames(60);
  }

  /// <summary>
  /// Runs before each test, so that each test counts its own failed checks.
  /// The runner takes one <see cref="TestPrepareAttribute"/> method per scenario, and fails a
  /// scenario that declares a second: what a test needs first goes at the start of the test.
  /// </summary>
  [TestPrepare]
  [UsedImplicitly]
  protected void ResetChecks() {
    this.failedChecks = 0;
    this.testIndex++;

    if (this.overlay is null) {
      var description = this.ToString();

      this.overlay = ProgressOverlay.Start(
        description[..description.LastIndexOf(" (", StringComparison.Ordinal)],
        this.tests.Length
      );
    }

    this.overlay.StartTest(this.testIndex, this.tests.ElementAtOrDefault(this.testIndex - 1) ?? "");
  }

  /// <summary>
  /// Shows a step of the current test on the progress overlay, such as one site of several.
  /// </summary>
  protected void Step(string name, int index, int count) => this.overlay?.Step(name, index, count);

  /// <summary>
  /// Logs a check as passed or failed, so that a test reports every check and not only the first
  /// failed one.
  /// </summary>
  protected void Check(bool passed, string text) {
    TestScenario.log.Info($"  [{(passed ? "PASSED" : "FAILED")}] {text}");
    this.overlay?.Report(passed, text);

    this.failedChecks += passed ? 0 : 1;
  }

  /// <summary>
  /// Fails the test when one of its checks failed: a test ends on it.
  /// The runner awaits a test returning a task, and reports the exception the task holds.
  /// </summary>
  protected Task Finish() =>
    this.failedChecks is 0
      ? Task.CompletedTask
      : Task.FromException(
        new TestScenarioException($"{this.failedChecks} checks failed, see the lines above.")
      );

  /// <summary>
  /// A test's name in words: <c>Sites_LayAsTheirLayout</c> reads "Sites: lay as their layout".
  /// </summary>
  private static string Words(string name) {
    var parts = name.Split('_');
    var spaced = parts.Select(static part => Regex.Replace(part, "(?<=[a-z0-9])(?=[A-Z])", " "));

    return string.Join(": ", spaced.Select((part, i) => i == 0 ? part : part.ToLowerInvariant()));
  }
}
