using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BreakwaterTool.Breakwaters;
using Colossal.Entities;
using Colossal.TestFramework;
using Colossal.UI.Binding;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.Rendering;
using Game.SceneFlow;
using Game.Tools;
using Game.UI;
using JetBrains.Annotations;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;
using Transform = Game.Objects.Transform;

namespace BreakwaterTool.Scenarios;

/// <summary>
/// The look in a new city: the asset menu's ways into the stones, the look kept for the next
/// session, the plants a breakwater clears and Revert puts back, and a slider on a slow breakwater.
/// The tests run in the order they are declared, each from the tool as the one before left it.
/// </summary>
[TestDescriptor($"{nameof(BreakwaterTool)}: Look", ScenarioRegistry.ScenarioCategory)]
[UsedImplicitly]
internal sealed class LookScenario : ModTestScenario {
  private const string ShownCategory = "toolbar.selectedAssetCategory";

  // Half the side of the square laid round a plant, in metres.
  private const float HalfSide = 10f;

  /// <summary>
  /// The game's toolbar bindings the Stones row reads and calls, and the clicks this scenario sends
  /// call.
  /// </summary>
  private static readonly string[] ToolbarBindings = [
    LookScenario.ShownCategory, "toolbar.selectAssetCategory", "toolbar.selectAsset"
  ];

  // The player's look, put back at the end: the scenario starts from none.
  private string userLook = "";

  private BreakwaterToolSystem Tool => this.World.GetExistingSystemManaged<BreakwaterToolSystem>();

  private BreakwaterSystem Breakwaters => this.World.GetExistingSystemManaged<BreakwaterSystem>();

  protected override Task OnPrepare() {
    this.userLook = Mod.Settings.LastLook;
    Mod.Settings.LastLook = "";

    return Task.CompletedTask;
  }

  protected override Task OnCleanup() {
    // Forgotten first, so that leaving the tool on the way out keeps none of the scenario's look.
    BreakwaterToolSystem.DebugForgetLook();
    BreakwaterToolSystem.DebugHoldPointer(null);
    Mod.Settings.LastLook = this.userLook;
    Mod.Settings.ApplyAndSave();

    return base.OnCleanup();
  }

  [Test]
  [UsedImplicitly]
  private async Task Toolbar_BindsWhatTheStonesRowCalls() {
    await this.StartNewCity(ModTestScenario.Archipelago, "BreakwaterTool look");

    var bindings = LookScenario.Bindings();

    foreach (var path in LookScenario.ToolbarBindings) {
      this.Check(
        bindings.ContainsKey(path),
        $"The game binds {path}. If not: find what the asset menu binds now, and follow it in " +
        "the Stones row's add tile (breakwater-tool.ts) and in this scenario."
      );
    }

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task AssetMenu_AddsStonesAndKeepsTheTool() {
    var toolSystem = this.World.GetExistingSystemManaged<ToolSystem>();
    // Other mods' tools may go ahead, as they do the same: a tool of the game's must not.
    var ahead = toolSystem.tools.Take(toolSystem.tools.IndexOf(this.Tool)).ToArray();

    this.Check(
      ahead.All(static tool => tool.GetType().Assembly != typeof(ToolSystem).Assembly),
      "The game offers a picked prefab to the tool ahead of its own tools; ahead of it: " +
      $"{string.Join(", ", ahead.Select(static tool => tool.GetType().FullName))}."
    );

    if (BreakwaterStyles.Find(BreakwaterStyles.Default) is not {} style) {
      throw new TestScenarioException($"No built-in style is named {BreakwaterStyles.Default}.");
    }

    // The tool reads the look at its activation, and none is kept.
    BreakwaterToolSystem.DebugForgetLook();
    this.Tool.Toggle();
    await this.WaitFrames(5);

    var stones = this.Breakwaters.Resolve(style.Stones);
    var boulder = stones.FirstOrDefault();
    var category = this.CategoryOf(boulder);

    this.Check(
      this.Tool.IsActive && category != Entity.Null && this.Tool.Choice.SequenceEqual(stones),
      "With no look kept, the tool takes the default style's stones: " +
      $"{BreakwaterToolSystem.DebugChoice()}, in {this.NameOf(category)}."
    );

    // What the Stones row's add tile sends.
    var changes = await this.RunScript(
      "engine.trigger('breakwaterTool.tool.expectOffer'); " +
      $"engine.trigger('toolbar.selectAssetCategory', {LookScenario.Js(category)});"
    );

    this.Check(
      this.Shown() == category &&
      this.Tool.IsActive &&
      changes == 0 &&
      this.Tool.Choice.SequenceEqual(stones),
      "The add tile opens the stones' category, the tool kept and the stones as they were: shown " +
      $"{this.NameOf(this.Shown())}, active {this.Tool.IsActive}, {changes} changes, stones " +
      $"{BreakwaterToolSystem.DebugChoice()}."
    );

    var stone = this.Prefabs<ObjectData>()
      .FirstOrDefault(prefab =>
        this.CategoryOf(prefab) == category &&
        this.Breakwaters.CanLay(prefab) &&
        !stones.Contains(prefab)
      );

    changes = await this.RunScript(LookScenario.SelectAsset(stone));

    this.Check(
      this.Tool.IsActive && changes == 1 && this.Tool.Choice.Contains(stone),
      $"A click on another stone of the category, {this.NameOf(stone)}, adds it, the tool kept: " +
      $"active {this.Tool.IsActive}, {changes} changes, " +
      $"stones {BreakwaterToolSystem.DebugChoice()}."
    );

    await this.CheckRefusal();

    // A terraforming prefab is no object, which only its own tool takes.
    var terraform = this.Prefabs<TerraformingData>()
      .FirstOrDefault(prefab => this.CategoryOf(prefab) != Entity.Null);

    await this.RunScript(LookScenario.SelectCategory(this.CategoryOf(terraform)));
    await this.RunScript(LookScenario.SelectAsset(terraform));

    this.Check(
      !this.Tool.IsActive && toolSystem.activeTool is TerrainToolSystem,
      $"A click on a terraforming prefab, {this.NameOf(terraform)}, leaves for the terrain tool: " +
      $"{toolSystem.activeTool.toolID}."
    );

    var thumbnail = this.World.GetExistingSystemManaged<PrefabSystem>()
      .TryGetPrefab<PrefabBase>(boulder, out var prefab)
      ? ImageSystem.GetThumbnail(prefab)
      : null;

    this.Check(
      !string.IsNullOrEmpty(thumbnail),
      $"The default style's stone has a thumbnail for the Stones row: {thumbnail}."
    );

    // An object the tool could lay, from another menu, as Find It offers one on a plain click.
    var prop = this.Prefabs<ObjectData>()
      .FirstOrDefault(other =>
        this.Breakwaters.CanLay(other) && !this.IsOfMenu(this.CategoryOf(other), category)
      );

    this.Tool.Toggle();
    await this.WaitFrames(5);

    // Entered by its key, the tool shows no category: a stone not among the stones goes to its own
    // tool, and the one Find It offers again as it reopens, its active prefab, which it keeps among
    // the stones, keeps the tool.
    var unchosen = this.Prefabs<ObjectData>()
      .FirstOrDefault(prefab =>
        this.CategoryOf(prefab) == category &&
        this.Breakwaters.CanLay(prefab) &&
        !this.Tool.Choice.Contains(prefab)
      );

    var isLeft = this.Offer(unchosen);

    await this.WaitFrames(5);

    this.Check(
      isLeft && !this.Tool.IsActive && toolSystem.activeTool is ObjectToolSystem,
      $"A stone not chosen, {this.NameOf(unchosen)}, offered with no category shown leaves for " +
      $"the object tool: {toolSystem.activeTool.toolID}."
    );

    this.Tool.Toggle();
    await this.WaitFrames(5);

    var chosen = this.Tool.Choice.ToArray();
    var isKept = this.Offer(stone);

    await this.WaitFrames(5);

    this.Check(
      isKept &&
      this.Tool.IsActive &&
      this.Tool.Choice.SequenceEqual(chosen) &&
      this.Shown() == category,
      $"A stone already chosen, {this.NameOf(stone)}, offered with no category shown keeps the " +
      $"tool and the stones, and the toolbar shows its category: active {this.Tool.IsActive}, " +
      $"{toolSystem.activeTool.toolID}, stones {BreakwaterToolSystem.DebugChoice()}, shown " +
      $"{this.NameOf(this.Shown())}."
    );

    // Among the stones too, which keeps the tool only while no category is shown.
    BreakwaterToolSystem.DebugAddStone(prop);

    var isActivated = this.Offer(prop);

    await this.WaitFrames(5);

    this.Check(
      isActivated && !this.Tool.IsActive && toolSystem.activeTool is ObjectToolSystem,
      $"An object of another menu, {this.NameOf(prop)}, leaves for the object tool though the " +
      $"tool could lay it and holds it among the stones: {toolSystem.activeTool.toolID}."
    );

    BreakwaterToolSystem.DebugRemoveStone(prop);
    toolSystem.activeTool = this.World.GetExistingSystemManaged<DefaultToolSystem>();
    await this.WaitFrames(5);

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task Look_IsKeptForTheNextSession() {
    this.Tool.Toggle();
    await this.WaitFrames(5);

    this.Tool.SetParameter(nameof(BreakwaterParameters.Tumble), 0.35f);

    var parameters = this.Tool.Parameters;
    var choice = this.Tool.Choice.ToArray();

    this.Check(
      this.Tool.IsActive &&
      this.Tool.Picked == Entity.Null &&
      parameters != BreakwaterParameters.Default &&
      choice.Length == 2,
      "The tool, nothing picked, holds a look of its own: " +
      $"{BreakwaterToolSystem.DebugLook()}, stones {BreakwaterToolSystem.DebugChoice()}."
    );

    this.Tool.Toggle();
    await this.WaitFrames(5);

    var kept = Mod.Settings.LastLook.Split('|');

    this.Check(
      !this.Tool.IsActive &&
      kept[0] == ParameterTable.Write(parameters) &&
      kept.ElementAtOrDefault(1)?.Split(',').Length == choice.Length,
      $"Leaving the tool keeps its look in the settings: {Mod.Settings.LastLook}."
    );

    // As a new session starts: the tool reads the look kept at its first activation.
    BreakwaterToolSystem.DebugForgetLook();
    this.Tool.Toggle();
    await this.WaitFrames(5);

    this.Check(
      this.Tool.Parameters == parameters && this.Tool.Choice.SequenceEqual(choice),
      "The next session's tool reads that look back: " +
      $"{BreakwaterToolSystem.DebugLook()}, stones {BreakwaterToolSystem.DebugChoice()}."
    );

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task ClearVegetation_RevertPutsBackThePlants() {
    var camera = this.World.GetExistingSystemManaged<CameraUpdateSystem>().activeCameraController;
    var entry = ((float3) camera.pivot).xz;

    // The free plant nearest to the city's entry on dry ground, which a new city has nothing on.
    var middle = this.Plants()
      .Select(static plant => plant.Transform.m_Position.xz)
      .Where(at => Album.SeaDepth(this.World, at) <= 0f)
      .OrderBy(at => math.distance(at, entry))
      .FirstOrDefault();

    var before = this.PlantsAround(middle);

    var outcome = BreakwaterApi.Create(
      [
        middle + new float2(-LookScenario.HalfSide, -LookScenario.HalfSide),
        middle + new float2(LookScenario.HalfSide, -LookScenario.HalfSide),
        middle + new float2(LookScenario.HalfSide, LookScenario.HalfSide),
        middle + new float2(-LookScenario.HalfSide, LookScenario.HalfSide)
      ],
      this.Tool.Choice
    );

    await this.WaitFrames(5);

    this.Check(
      outcome.WhyNothing is null && before.Length > 0,
      $"A breakwater is laid round {before.Length} plants at ({middle.x:F0}, {middle.y:F0}): " +
      $"{outcome.WhyNothing ?? "laid"}."
    );

    BreakwaterToolSystem.DebugPick(outcome.Breakwater);
    this.Tool.ClearPickedVegetation();
    await this.WaitFrames(5);

    var left = this.PlantsAround(middle).Length;

    this.Check(left == 0, $"Clear vegetation removes the plants inside: {left} left.");

    BreakwaterToolSystem.DebugRevert();
    await this.WaitFrames(5);

    var after = this.PlantsAround(middle);
    var same = before.Count(after.Contains);

    this.Check(
      after.Length == before.Length && same == before.Length,
      "Revert puts back the plants, each with its prefab, place, growth and seed: " +
      $"{after.Length} of {before.Length} back, {same} the same."
    );

    await this.Finish();
  }

  [Test]
  [UsedImplicitly]
  private async Task Slider_OnASlowBreakwater_LaysOnceLetGo() {
    var record = this.Tool.Picked;
    var laid = BreakwaterApi.Read(record)?.Parameters.Irregularity;

    // As slow as a breakwater of thousands of stones, under a drag held still.
    BreakwaterToolSystem.DebugLayoutCost(record, 500.0);
    BreakwaterToolSystem.DebugHoldPointer(true);
    this.Tool.SetParameter(nameof(BreakwaterParameters.Irregularity), 0.05f);
    await this.WaitFrames(1);

    var moved = BreakwaterApi.Read(record)?.Parameters.Irregularity;

    this.Check(
      this.Tool.Parameters.Irregularity is 0.05f && moved == laid,
      $"A slider's value shows at once, {this.Tool.Parameters.Irregularity}, and the breakwater " +
      $"keeps {moved}."
    );

    await this.WaitSeconds(1f);

    var held = BreakwaterApi.Read(record)?.Parameters.Irregularity;

    this.Check(held == laid, $"Held still past the rest, the breakwater keeps {held}.");

    BreakwaterToolSystem.DebugHoldPointer(false);
    await this.WaitFrames(5);

    var rested = BreakwaterApi.Read(record)?.Parameters.Irregularity;

    BreakwaterToolSystem.DebugHoldPointer(null);
    this.Check(rested is 0.05f, $"Let go, the breakwater is laid with {rested}.");

    // A release, then the tool left inside the wait.
    this.Tool.SetParameter(nameof(BreakwaterParameters.Irregularity), 0.06f);
    this.Tool.Toggle();
    await this.WaitFrames(5);

    var left = BreakwaterApi.Read(record)?.Parameters.Irregularity;

    this.Check(
      !this.Tool.IsActive && left is 0.06f,
      $"Leaving the tool inside the wait lays the value: {left}."
    );

    await this.Finish();
  }

  /// <summary>
  /// The button a slider's wait reads, pressed and released where a click does nothing: the tool
  /// left, the pointer on the screen's top edge.
  /// </summary>
  [Test]
  [UsedImplicitly]
  private async Task HeldPointer_IsTheMousesPrimaryButton() {
    using var input = new NativeInput();

    input.MovePointer(new Vector2(Screen.width / 2f, Screen.height - 1f));
    await this.WaitFrames(3);
    input.SetButton(MouseButton.Left, true);
    await this.WaitFrames(3);

    var pressed = BreakwaterToolSystem.DebugIsPointerHeld();

    input.SetButton(MouseButton.Left, false);
    await this.WaitFrames(3);

    var released = BreakwaterToolSystem.DebugIsPointerHeld();

    this.Check(
      pressed && !released,
      $"A slider's wait reads the mouse's primary button: held {pressed} once pressed, " +
      $"{released} once released. If not: find how the input system tells that button now, and " +
      "read it in BreakwaterToolSystem.IsPointerHeld."
    );

    await this.Finish();
  }

  /// <summary>
  /// A style sets the stones while they are the ones a style set, and leaves stones chosen by hand,
  /// even when the hand chose another style's set.
  /// </summary>
  [Test]
  [UsedImplicitly]
  private async Task Style_SetsTheStonesUntilTheyAreChosenByHand() {
    // A new session with no look kept, which starts on the default style's stones.
    BreakwaterToolSystem.DebugForgetLook();
    Mod.Settings.LastLook = "";
    this.Tool.Toggle();
    await this.WaitFrames(5);

    var first = BreakwaterStyles.All[0];
    var firstStones = this.Breakwaters.Resolve(first.Stones);
    var second = BreakwaterStyles.All.First(style =>
      !this.Breakwaters.Resolve(style.Stones).SequenceEqual(firstStones)
    );
    var secondStones = this.Breakwaters.Resolve(second.Stones);

    BreakwaterToolSystem.DebugApplyStyle(second.Name);

    var isSecond = this.IsChoice(secondStones);

    BreakwaterToolSystem.DebugApplyStyle(first.Name);

    var isFirst = this.IsChoice(firstStones);

    this.Check(
      this.Tool.IsActive && isSecond && isFirst,
      $"A style sets the stones a style set: {second.Name}'s {isSecond}, then {first.Name}'s " +
      $"{isFirst}."
    );

    foreach (var stone in secondStones) {
      BreakwaterToolSystem.DebugAddStone(stone);
    }

    foreach (var stone in firstStones.Except(secondStones)) {
      BreakwaterToolSystem.DebugRemoveStone(stone);
    }

    BreakwaterToolSystem.DebugApplyStyle(first.Name);

    this.Check(
      this.IsChoice(secondStones) && this.Tool.Style == first.Name,
      $"Stones chosen by hand stay through a style, though they are {second.Name}'s set: " +
      $"{BreakwaterToolSystem.DebugChoice()}, style {this.Tool.Style}."
    );

    // A breakwater picked on a style's stones, given another style's, then reverted: its stones are
    // the first style's again, which the next style replaces.
    var record = BreakwaterApi.List()[0];
    var edited = BreakwaterApi.Edit(record, stonePrefabs: firstStones);

    BreakwaterToolSystem.DebugPick(record);
    BreakwaterToolSystem.DebugApplyStyle(second.Name);
    BreakwaterToolSystem.DebugRevert();

    var isReverted = this.IsChoice(firstStones);

    BreakwaterToolSystem.DebugApplyStyle(second.Name);

    this.Check(
      edited is null && isReverted && this.IsChoice(secondStones),
      $"A style replaces the stones a revert put back: {first.Name}'s set {edited ?? "laid"}, " +
      $"reverted to it {isReverted}, then {BreakwaterToolSystem.DebugChoice()}."
    );

    await this.Finish();
  }

  /// <summary>
  /// Find It's Shift-click, which deselects the stones outside its range, then selects the range.
  /// </summary>
  [Test]
  [UsedImplicitly]
  private async Task FindIt_RangeSelect_SetsTheStonesInOneLay() {
    var prefabSystem = this.World.GetExistingSystemManaged<PrefabSystem>();
    var record = BreakwaterApi.List()[0];

    BreakwaterToolSystem.DebugPick(record);
    await this.WaitFrames(5);

    var chosen = this.Tool.Choice.ToArray();
    var category = this.CategoryOf(chosen[0]);
    var range = this.Prefabs<ObjectData>()
      .Where(prefab =>
        this.CategoryOf(prefab) == category &&
        this.Breakwaters.CanLay(prefab) &&
        !chosen.Contains(prefab)
      )
      .Take(2)
      .ToArray();

    var revision = this.Breakwaters.Revision;

    foreach (var stone in chosen) {
      this.Tool.DeselectPrefab(prefabSystem.GetPrefab<PrefabBase>(stone));
    }

    foreach (var stone in range) {
      this.Tool.SelectPrefab(prefabSystem.GetPrefab<PrefabBase>(stone));
    }

    await this.WaitFrames(5);

    var laid = BreakwaterApi.Read(record)?.StonePrefabs ?? [];
    var lays = this.Breakwaters.Revision - revision;

    this.Check(
      range.Length == 2 &&
      this.Tool.Choice.SequenceEqual(range) &&
      laid.SequenceEqual(range) &&
      lays <= 2,
      $"A range selected in Find It sets the stones to it in one lay: stones " +
      $"{BreakwaterToolSystem.DebugChoice()}, {laid.Length} laid, revision +{lays}."
    );

    await this.Finish();
  }

  private bool IsChoice(Entity[] stones) =>
    this.Tool.Choice.Count == stones.Length && stones.All(this.Tool.Choice.Contains);

  /// <summary>
  /// Every binding the game's UI holds, by path, through the composites systems nest.
  /// </summary>
  private static Dictionary<string, BindingBase> Bindings() {
    var bindings = new Dictionary<string, BindingBase>();

    Collect(GameManager.instance.userInterface.bindings);

    return bindings;

    void Collect(IBindingRegistry registry) {
      if (registry is not CompositeBinding composite) {
        return;
      }

      foreach (var binding in composite.bindings) {
        if (binding is BindingBase named) {
          bindings[named.path] = named;
        }

        if (binding is IBindingRegistry nested) {
          Collect(nested);
        }
      }
    }
  }

  private static string SelectCategory(Entity category) =>
    $"engine.trigger('toolbar.selectAssetCategory', {LookScenario.Js(category)});";

  private static string SelectAsset(Entity prefab) =>
    $"engine.trigger('toolbar.selectAsset', {LookScenario.Js(prefab)}, true);";

  /// <summary>
  /// Offers a prefab to the tools as the game's lists and Find It do: whether a tool took it.
  /// </summary>
  private bool Offer(Entity prefab) =>
    this.World.GetExistingSystemManaged<PrefabSystem>()
      .TryGetPrefab<PrefabBase>(prefab, out var offered) &&
    this.World.GetExistingSystemManaged<ToolSystem>().ActivatePrefabTool(offered);

  /// <summary>
  /// An entity as the UI sends it.
  /// </summary>
  private static string Js(Entity entity) =>
    $"{{ index: {entity.Index}, version: {entity.Version} }}";

  /// <summary>
  /// Opens a category of the stones' menu holding no stone, and clicks an object of it: the tool
  /// refuses the object, and stays.
  /// </summary>
  private async Task CheckRefusal() {
    var stones = this.Shown();
    var choice = this.Tool.Choice.ToArray();

    var tree = this.Prefabs<TreeData>()
      .FirstOrDefault(prefab =>
        this.IsOfMenu(this.CategoryOf(prefab), stones) && !this.Breakwaters.CanLay(prefab)
      );

    var trees = this.CategoryOf(tree);
    var changes = await this.RunScript(LookScenario.SelectCategory(trees));

    this.Check(
      tree != Entity.Null && this.Tool.IsActive && changes == 0,
      $"Opening {this.NameOf(trees)}, of the stones' menu, keeps the tool: active " +
      $"{this.Tool.IsActive}, {changes} changes."
    );

    changes = await this.RunScript(LookScenario.SelectAsset(tree));

    this.Check(
      this.Tool.IsActive &&
      this.Tool.Refusal?.id == "BreakwaterTool.Tool.REFUSED[NotAStone]" &&
      changes == 0 &&
      this.Tool.Choice.SequenceEqual(choice),
      $"A click on {this.NameOf(tree)} is refused, the tool kept: active {this.Tool.IsActive}, " +
      $"refusal {this.Tool.Refusal?.id}, {changes} changes."
    );
  }

  /// <summary>
  /// Waits for a span of real time, which a wait in frames cannot promise.
  /// </summary>
  private async Task WaitSeconds(float seconds) {
    var end = Time.realtimeSinceStartup + seconds;

    while (Time.realtimeSinceStartup < end) {
      await this.WaitFrames(1);
    }
  }

  /// <summary>
  /// Runs a script in the game's UI, as a click there does, and returns how many times the look
  /// changed meanwhile.
  /// </summary>
  private async Task<uint> RunScript(string script) {
    var revision = this.Tool.LookRevision;

    GameManager.instance.userInterface.view.View.ExecuteScript(script);
    await this.WaitFrames(5);

    return this.Tool.LookRevision - revision;
  }

  /// <summary>
  /// The asset menu category the toolbar shows, as the Stones row reads it.
  /// </summary>
  private Entity Shown() =>
    LookScenario.Bindings().GetValueOrDefault(LookScenario.ShownCategory)
      is ValueBinding<Entity> shown
      ? shown.value
      : Entity.Null;

  /// <summary>
  /// The prefabs of the asset menu that hold a component.
  /// </summary>
  private Entity[] Prefabs<T>() where T : unmanaged, IComponentData {
    using var query = this.World.EntityManager.CreateEntityQuery(
      ComponentType.ReadOnly<UIObjectData>(),
      ComponentType.ReadOnly<T>()
    );

    using var prefabs = query.ToEntityArray(Allocator.Temp);

    return prefabs.ToArray();
  }

  private Entity CategoryOf(Entity prefab) =>
    this.World.EntityManager.TryGetComponent<UIObjectData>(prefab, out var item)
      ? item.m_Group
      : Entity.Null;

  private bool IsOfMenu(Entity category, Entity other) =>
    this.World.EntityManager.TryGetComponent<UIAssetCategoryData>(category, out var data) &&
    this.World.EntityManager.TryGetComponent<UIAssetCategoryData>(other, out var otherData) &&
    data.m_Menu != Entity.Null &&
    data.m_Menu == otherData.m_Menu;

  private string NameOf(Entity prefab) =>
    this.World.GetExistingSystemManaged<PrefabSystem>()
      .TryGetPrefab<PrefabBase>(prefab, out var named)
      ? named.name
      : prefab.ToString();

  /// <summary>
  /// The plants of the map that belong to no building or road, as the breakwater clears them.
  /// </summary>
  private PlantState[] Plants() {
    var manager = this.World.EntityManager;

    using var query = manager.CreateEntityQuery(
      ComponentType.ReadOnly<Plant>(),
      ComponentType.ReadOnly<Transform>(),
      ComponentType.Exclude<Owner>(),
      ComponentType.Exclude<Deleted>(),
      ComponentType.Exclude<Temp>()
    );

    using var plants = query.ToEntityArray(Allocator.Temp);

    return [
      .. plants.ToArray()
        .Select(plant => new PlantState(
            manager.GetComponentData<PrefabRef>(plant).m_Prefab,
            manager.GetComponentData<Transform>(plant),
            manager.TryGetComponent<PseudoRandomSeed>(plant, out var seed)
              ? seed.m_Seed
              : (ushort) 0,
            manager.TryGetComponent<Tree>(plant, out var tree) ? tree : null,
            manager.HasComponent<Native>(plant)
          )
        )
    ];
  }

  /// <summary>
  /// The free plants inside the square laid round a point.
  /// </summary>
  private PlantState[] PlantsAround(float2 middle) => [
    .. this.Plants()
      .Where(plant =>
        math.all(math.abs(plant.Transform.m_Position.xz - middle) < LookScenario.HalfSide)
      )
  ];
}
