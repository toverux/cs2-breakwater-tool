---
name: bwt-game-testing
description: Write and run the mod's in-game test scenarios, which the game's own test framework runs in the live game. Use when a check needs the running game (systems over live ECS state, a city loaded), when writing or changing a scenario, when a scenario run fails, or when setting up the game to look at layouts by eye.
---

# In-game testing

An in-game **scenario** is a class the game's test framework (`Colossal.TestFramework`) runs inside the game: it loads what it needs, drives the mod's systems, and logs one line per check to `Logs/TestScenarios.log` in the game's user data folder.
It is the slowest layer, a full game boot per run: pin here only what needs the live game, and test pure logic off-engine.
The `cs2-modding` skill's game cycle holds the framework's traps, a success reported for a scenario that ran no test among them: read it before writing a scenario or reading a run.

## Run

1. Deploy a Debug build of the current sources, the only build carrying the scenarios: `stop` the game when it runs, then `mise build`.
2. Call the `cs2-dev` `test` tool with `mod` set to `BreakwaterTool` and `sources` to this repository's `BreakwaterTool` folder, as an absolute path. Its default `category` is the mod's.

`keepOpen` leaves the game running after the run, for a look with the unity eval or at what the scenario left.

Two other ways start a scenario in a game already running a Debug build:

- the developer menu (launched with `--developerMode --qaDeveloperMode`): its Test Scenarios tab lists the mod's scenarios under the category's number;
- the unity eval: `Colossal.TestFramework.TestScenarioSystem.instance.RunScenario("BreakwaterTool: Proving ground", System.Threading.CancellationToken.None)`.

## Writing a scenario

Copy the shape of [`ProvingGroundScenario`](../../../BreakwaterTool/Scenarios/ProvingGroundScenario.cs):

- A class under `BreakwaterTool/Scenarios/`, derived from `ModTestScenario`, with `[TestDescriptor("BreakwaterTool: <name>", ScenarioRegistry.ScenarioCategory)]`.
  The csproj compiles that folder in Debug only, and [`ScenarioRegistry`](../../../BreakwaterTool/Scenarios/ScenarioRegistry.cs), called from `Mod.OnLoad`, adds every scenario of the assembly to the framework.
- The framework runs, in order: the constructor, `OnPrepare`, then for each `[Test]` method its `[TestPrepare]` method, the test and its `[TestCleanup]` method, then `OnCleanup`, whatever failed before.
  Test methods are private, return `void` or `Task` (a returned task is awaited, and the exception it holds fails the test) and carry `[UsedImplicitly]`.
  The framework takes one `[TestPrepare]` method per scenario, and `ModTestScenario` holds it: what a test needs first goes at the start of the test.
- Start a new city with `await this.StartNewCity(ModTestScenario.Archipelago, <city name>)`, which sets what a new city reads at creation, its starting year among them (the current one unless passed), and returns once the load has ended. Archipelago is in the base game; no save ships with the mod.
- **Start every other load from the game loop**, as `StartNewCity` does: `await this.WaitFrames(2)` before `GameManager.instance.Load`. A load started from the scenario's caller, the unity eval for one, holds the main thread the load needs, and the game must be killed.
- Put back in `OnCleanup` whatever the scenario changed that outlives it (a setting, a saved value, the Continue a save moves), then return `base.OnCleanup()`, which returns to the main menu.
  A throw in `OnCleanup` goes unobserved and can kill the game.
- [`ModTestScenario`](../../../BreakwaterTool/Scenarios/ModTestScenario.cs) dismisses in its constructor the errors pending from before the scenario, which would abort it, and logs how many; an error of the mod's among them still fails the `test` run.
- Report each check with `this.Check(<passed>, <what was read>)`, which logs a `[PASSED]` or `[FAILED]` line, and end each test on `this.Finish()`, which fails it when one of its checks failed: a run reports every check and not only the first.
- Keep a check's expected values in the mod's code, beside the logic, when an off-engine test can check them too: one table that the off-engine tests and the scenario both read.
- A long test names its steps on the overlay with `this.Step(name, index, count)`. `ProvingGroundScenario` is the worked example of a save round trip (`SaveMap`, `SaveCity`, `Load`), a terrain edit (`Raise`) and the exact city check (`CityStones`); lift a helper to `ModTestScenario` when a second scenario needs it.

## The category

Every scenario carries `ScenarioRegistry.ScenarioCategory`, `1 << 20`, the `test` tool's default `category`: a bit no category of the game's uses, so the run leaves out the game's scenarios and other mods' `Category.QA` ones.
On a change of that bit, pass the new number as `category`.

## Looking by eye

Look at layouts on the proving ground in the editor: the main menu's Editor, then `BreakwaterTool.Scenarios.Ground.ProvingGroundLoader.Apply(world)` from the unity eval, the sea set as `ProvingGroundScenario`'s album sets it, and each breakwater fitted to the new terrain.
Load it into the editor's new map, never under a city: a city keeps its roads and trees at their old heights, standing on walls of terrain.
Zoom in before judging a layout's density: the editor stops drawing small objects from a camera zoom of about 95 m, which reads as a sparse layout.
Toggle the tool on and pick a breakwater in separate evals: the tool's `OnStartRunning` runs at its next update, unpicks, and loads the saved look over the pick's.
