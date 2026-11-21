using System.Linq;
using System.Threading.Tasks;
using BreakwaterTool.Breakwaters;
using BreakwaterTool.Gestures;
using BreakwaterTool.Scenarios.Ground;
using BreakwaterTool.Systems;
using Colossal.TestFramework;
using Game.Prefabs;
using Game.Rendering;
using Game.SceneFlow;
using Game.Tools;
using JetBrains.Annotations;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace BreakwaterTool.Scenarios;

/// <summary>
/// Draws, picks and edits a breakwater with the tool in a new city, through the pointer and the
/// mouse buttons the player's devices send, and reads back what each gesture laid.
/// The tests run in the order they are declared, each from the breakwater the one before left.
/// </summary>
[TestDescriptor($"{nameof(BreakwaterTool)}: Tool", ScenarioRegistry.ScenarioCategory)]
[UsedImplicitly]
internal sealed class ToolScenario : ModTestScenario {
  /// <summary>
  /// How far a vertex laid may be from where the pointer put it, in metres: a pixel spans about a
  /// tenth of a metre at the camera's distance.
  /// </summary>
  private const float Tolerance = 0.5f;

  // The square drawn, its corners from the site's middle.
  private static readonly float2[] Square = [
    new(-12f, -12f), new(12f, -12f), new(12f, 12f), new(-12f, 12f)
  ];

  // Where the drag takes the square's second corner.
  private static readonly float2 Dragged = new(18f, -16f);

  // The middle of the square's third edge, where the insert presses, and where it lets go.
  private static readonly float2 OnEdge = new(0f, 12f);

  private static readonly float2 Inserted = new(0f, 20f);

  // Created by the first test, disposed at the end: one instance holds the buttons down across a
  // drag.
  private NativeInput? input;

  private float2 middle;

  private Entity record;

  private BreakwaterToolSystem Tool => this.World.GetExistingSystemManaged<BreakwaterToolSystem>();

  private BreakwaterSystem Breakwaters => this.World.GetExistingSystemManaged<BreakwaterSystem>();

  protected override Task OnCleanup() {
    this.input?.Dispose();

    return base.OnCleanup();
  }

  [Test]
  [UsedImplicitly]
  private async Task Draw_ClosesIntoAPickedBreakwater() {
    await this.StartNewCity(ModTestScenario.Archipelago, "BreakwaterTool tool");

    // A new city's camera looks at its entry, on dry ground with nothing built yet.
    var camera = this.World.GetExistingSystemManaged<CameraUpdateSystem>().activeCameraController;

    this.middle = ((float3) camera.pivot).xz;

    this.Check(
      Album.SeaDepth(this.World, this.middle) <= 0f,
      $"The site at ({this.middle.x:F0}, {this.middle.y:F0}) is dry ground."
    );

    // Looking down steeply, the site a little above the screen's middle, clear of the toolbar.
    camera.pivot = new Vector3(
      this.middle.x,
      Terraform.Height(this.World, this.middle),
      this.middle.y - 8f
    );
    camera.zoom = 100f;
    camera.rotation = new Vector3(60f, 0f, 0f);

    this.input = new NativeInput();
    this.Tool.Toggle();

    // A press in the frame the tool activates is skipped.
    await this.WaitFrames(5);

    this.Check(this.Tool.IsActive, "The tool is active.");

    var before = this.Breakwaters.List().Length;

    foreach (var corner in ToolScenario.Square) {
      await this.Click(corner, MouseButton.Left);
    }

    await this.Click(ToolScenario.Square[0], MouseButton.Left);

    this.record = this.Tool.Picked;

    var created = this.Breakwaters.List().Length - before;

    this.Check(
      created == 1 && this.record != Entity.Null,
      $"The close created a breakwater and picked it: {created} created, picked {this.record}."
    );
    this.CheckOutline("Closed", ToolScenario.Square);

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task Pick_FollowsTheButtonsInsideTheOutline() {
    await this.Click(float2.zero, MouseButton.Right);

    this.Check(
      this.Tool.Picked == Entity.Null,
      $"The secondary button inside leaves the pick: picked {this.Tool.Picked}."
    );

    await this.Click(float2.zero, MouseButton.Left);

    this.Check(
      this.Tool.Picked == this.record,
      $"The primary button inside picks it: picked {this.Tool.Picked}, as {this.record}."
    );

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task Drag_LaysTheVertexWhereItIsLetGo() {
    await this.Drag(ToolScenario.Square[1], ToolScenario.Dragged);

    this.CheckOutline(
      "Dragged",
      [
        .. ToolScenario.Square.Select((corner, i) =>
          i == 1 ? ToolScenario.Dragged : corner
        )
      ]
    );

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task Insert_LaysANewVertexOnTheEdgePressed() {
    await this.Drag(ToolScenario.OnEdge, ToolScenario.Inserted);

    float2[] square = [
      .. ToolScenario.Square.Select((corner, i) =>
        i == 1 ? ToolScenario.Dragged : corner
      )
    ];

    this.CheckOutline("Inserted", [.. square.Take(3), ToolScenario.Inserted, square[3]]);

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task Remove_LaysTheOutlineWithoutTheVertex() {
    await this.Click(ToolScenario.Inserted, MouseButton.Right);

    this.CheckOutline(
      "Removed",
      [
        .. ToolScenario.Square.Select((corner, i) =>
          i == 1 ? ToolScenario.Dragged : corner
        )
      ]
    );

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task Leave_KeepsTheBreakwaterAndDropsTheOutlineNotClosed() {
    var stones = this.Breakwaters.Stones(this.record).Count;

    // Two points of another outline, on open ground east of the breakwater.
    await this.Click(new float2(32f, -6f), MouseButton.Left);
    await this.Click(new float2(32f, 6f), MouseButton.Left);

    var (drawing, drawn) = BreakwaterToolSystem.DebugGestures();

    this.Check(
      drawing is GestureMode.Drawing && drawn.Length == 2,
      $"Another outline is drawn: {drawing}, {drawn.Length} points."
    );

    this.Tool.Toggle();

    await this.WaitFrames(5);

    this.Check(!this.Tool.IsActive, "The tool is left.");
    this.Check(
      this.Breakwaters.Read(this.record) is not null &&
      this.Breakwaters.Stones(this.record).Count == stones,
      $"The breakwater stays, with its {stones} stones: " +
      $"{this.Breakwaters.Stones(this.record).Count} now."
    );

    this.Tool.Toggle();

    await this.WaitFrames(5);

    var (mode, points) = BreakwaterToolSystem.DebugGestures();

    this.Check(
      this.Tool.IsActive && mode is GestureMode.Idle && points.Length == 0,
      $"Back in the tool, the outline not closed is gone: {mode}, {points.Length} points."
    );

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task Hints_ReadTheGamesLabels() {
    var labels = BreakwaterToolSystem.DebugLabels();
    var dictionary = GameManager.instance.localizationManager.activeDictionary;

    this.Check(labels.Count > 0, $"The tool read {labels.Count} of the game's labels.");

    foreach (var label in labels) {
      dictionary.TryGetValue($"Common.ACTION[{label.Key}]", out var text);

      this.Check(
        label.Value is not null && !string.IsNullOrEmpty(text),
        $"The label \"{label.Key}\" reads \"{text}\". If not: the game renamed it in its " +
        "\"Input/Tool Input Actions\"; read the new name there and change it in " +
        "BreakwaterToolSystem.Hints."
      );
    }

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task Landscaping_EntersTheTool() {
    var prefab = this.Tool.Prefab;
    var category = prefab is not null && prefab.TryGet<UIObject>(out var item)
      ? item.m_Group as UIAssetCategoryPrefab
      : null;

    this.Check(
      category is { name: BreakwaterToolUISystem.LandscapingCategory, m_Menu.name: "Landscaping" },
      $"The tool's item is in the Landscaping menu's {category?.name} category. If not: the game " +
      "moved its terrain tools; change " +
      $"{nameof(BreakwaterToolUISystem)}.{nameof(BreakwaterToolUISystem.LandscapingCategory)}."
    );

    var toolSystem = this.World.GetExistingSystemManaged<ToolSystem>();

    toolSystem.activeTool = this.World.GetExistingSystemManaged<DefaultToolSystem>();

    await this.WaitFrames(5);

    // What a click on the menu's item runs.
    var isActivated = prefab is not null && toolSystem.ActivatePrefabTool(prefab);

    await this.WaitFrames(5);

    this.Check(
      isActivated && this.Tool.IsActive && this.Tool.GetPrefab() == prefab,
      $"The item activates the tool and stays selected: activated {isActivated}, active " +
      $"{this.Tool.IsActive}, selected {this.Tool.GetPrefab()?.name}."
    );

    await this.Finish();
  }

  /// <summary>
  /// Presses and releases a button with the pointer over a point of the site, then waits for what
  /// the click laid.
  /// </summary>
  private async Task Click(float2 at, MouseButton button) {
    await this.MoveTo(at);

    this.input!.SetButton(button, true);

    await this.WaitFrames(3);

    this.input.SetButton(button, false);

    await this.WaitFrames(10);
  }

  /// <summary>
  /// Presses the primary button over a point of the site, moves the pointer to another in steps, as
  /// a hand does, and releases it there, then waits for what the release laid.
  /// </summary>
  private async Task Drag(float2 from, float2 to) {
    const int steps = 6;

    await this.MoveTo(from);

    this.input!.SetButton(MouseButton.Left, true);

    await this.WaitFrames(3);

    for (var step = 1; step <= steps; step++) {
      await this.MoveTo(math.lerp(from, to, step / (float) steps));
    }

    this.input.SetButton(MouseButton.Left, false);

    await this.WaitFrames(10);
  }

  /// <summary>
  /// Moves the pointer over a point of the site, given from its middle, and waits for the tool to
  /// read it: the tool reads the raycast of the frame before.
  /// </summary>
  private async Task MoveTo(float2 at) {
    var ground = this.middle + at;

    if (BreakwaterToolSystem.DebugScreenPoint(ground.x, ground.y) is not {} screen) {
      throw new TestScenarioException($"The point {at} of the site is behind the camera.");
    }

    this.input!.MovePointer(screen);

    await this.WaitFrames(3);
  }

  /// <summary>
  /// Checks that the breakwater's outline is the one drawn, its vertices given from the site's
  /// middle.
  /// </summary>
  private void CheckOutline(string step, float2[] drawn) {
    var polygon = this.Breakwaters.Read(this.record)?.Polygon ?? [];
    var isAsDrawn = polygon.Length == drawn.Length &&
      polygon.Zip(drawn, (laid, at) => math.distance(laid, this.middle + at))
        .All(static distance =>
          distance <= ToolScenario.Tolerance
        );

    var laid = string.Join(
      " ",
      polygon.Select(vertex => vertex - this.middle).Select(static at => $"({at.x:F1}, {at.y:F1})")
    );
    var stones = this.Breakwaters.Stones(this.record).Count;

    this.Check(isAsDrawn, $"{step}: the outline laid is {laid}, as drawn.");
    this.Check(stones > 0, $"{step}: the breakwater holds {stones} stones.");
  }
}
