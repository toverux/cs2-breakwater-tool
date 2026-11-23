using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BreakwaterTool.Gestures;
using BreakwaterTool.Layout;
using Game;
using Game.Audio;
using Game.Common;
using Game.Input;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Game.UI.Editor;
using Game.UI.Localization;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BreakwaterTool.Breakwaters {
  /// <summary>
  /// Breakwater Tool, a tool of the game: it draws a breakwater's outline on the terrain, under
  /// water too, and picks and reshapes the city's breakwaters.
  /// Its rules are the gesture interpreter's: this system feeds it the pointer, applies what it
  /// returns through the breakwater operations, plays the vanilla area tool's sounds, sets the
  /// hints, and draws the overlay.
  /// Leaving it keeps every breakwater and drops an outline not closed yet.
  /// </summary>
  internal sealed partial class BreakwaterToolSystem : ToolBaseSystem {
    /// <summary>
    /// The tool's id, which the UI compares with the active tool's.
    /// </summary>
    internal const string Id = "BreakwaterTool";

    /// <summary>
    /// The room an outline's box keeps above and below its ground, in metres.
    /// </summary>
    private const float BoxMargin = 100f;

    // The city's breakwaters, each with the box its outline lies in, read again when the
    // operations' revision moves, and the toe edges of those whose toe is picked by hand.
    private readonly List<(Entity Record, float2[] Outline, Bounds Box)> city = [];

    // The breakwaters in view this frame, which the overlay draws, and those of them the pointer's
    // ray passes, which a gesture can act on.
    private readonly List<(Entity Record, float2[] Outline, Bounds Box)> inView = [];

    private readonly List<(Entity Record, float2[] Outline)> outlines = [];

    private readonly Plane[] frustum = new Plane[6];

    private readonly Dictionary<Entity, bool[]> toes = [];

    private readonly GestureInterpreter<Entity> gestures;

    private uint outlinesRevision = uint.MaxValue;

    // Created in OnCreate, which the game runs before any other member: hence mutable, and null!
    // until then.
    private BreakwaterSystem breakwaters = null!;

    private TerrainSystem terrainSystem = null!;

    private WaterSystem waterSystem = null!;

    private ProxyAction toggleAction = null!;

    private EntityQuery soundQuery;

    // Where the pointer is this frame.
    private Pointer<Entity> pointer;

    // The picked breakwater as it stood before the first edit of this pick, which Revert restores.
    private BreakwaterSnapshot? snapshot;

    // The toe of the outline drawn or picked, judged each frame; empty under three points.
    private bool[] toe = [];

    // [PERF] The terrain's heights, read once a frame, in place of once by each part of the tool
    // that reads them.
    private TerrainHeightData heights;

    // Whether the Landscaping menu entered the tool, until it is left: only then does the menu
    // show, with the tool's item selected; the other entries leave it closed.
    private bool isEnteredFromMenu;

    public BreakwaterToolSystem() {
      this.gestures = new GestureInterpreter<Entity>(
        this.outlines,
        this.HeightAt,
        record => this.toes.GetValueOrDefault(record),
        this.ChooseToe
      );
    }

    public override string toolID => BreakwaterToolSystem.Id;

    /// <summary>
    /// The tool's item in the Landscaping menu, null until the prefabs are loaded or where the menu
    /// lacks the category it goes in.
    /// </summary>
    internal BreakwaterToolPrefab? Prefab { get; set; }

    /// <summary>
    /// The hint at the pointer: the gestures its place allows.
    /// </summary>
    internal GestureHint Hint { get; private set; }

    /// <summary>
    /// Why the last gesture did nothing, until the next one does something; null when it did.
    /// </summary>
    internal LocalizedString? Refusal { get; private set; }

    /// <summary>
    /// The ground under the breakwater picked, as it is this frame: the player is told to shape a
    /// mound first where it is level or under water.
    /// </summary>
    internal GroundVerdict Ground { get; private set; }

    /// <summary>
    /// The breakwater picked, or <see cref="Entity.Null"/>.
    /// </summary>
    internal Entity Picked => this.gestures.Picked ?? Entity.Null;

    /// <summary>
    /// Whether the picked breakwater changed since it was picked, which Revert undoes.
    /// </summary>
    internal bool CanRevert => this.snapshot is not null;

    /// <summary>
    /// Whether the tool is the active one.
    /// </summary>
    internal bool IsActive => this.m_ToolSystem.activeTool == this;

    /// <summary>
    /// The tool's button in the editor's toolbar, null outside the editor.
    /// </summary>
    internal EditorTool? EditorButton { get; set; }

    /// <summary>
    /// Activates the tool, or leaves it for the default tool when it is active.
    /// </summary>
    internal void Toggle() {
      // In the editor through its toolbar, as its button does: an editor tool stores its state from
      // the tool system as it is disabled, so the active tool must still be its own then, or the
      // Object tool forgets its prefab (decompile: EditorPrefabTool.OnDisable).
      if (this.EditorButton is {} button) {
        this.World.GetOrCreateSystemManaged<EditorToolUISystem>().activeTool =
          this.IsActive ? null : button;

        return;
      }

      this.m_ToolSystem.activeTool = this.IsActive ? this.m_DefaultToolSystem : this;
    }

    /// <summary>
    /// Lays the picked breakwater again on the terrain as it now is.
    /// </summary>
    internal void FitPicked() {
      if (this.gestures.Picked is not {} picked) {
        return;
      }

      if (this.EditPicked(picked, new BreakwaterChange()) is null) {
        this.PlaySound(static sounds => sounds.m_PolygonToolFinishAreaSound);
      }
      else {
        this.PlaySound(static sounds => sounds.m_PlaceBuildingFailSound);
      }
    }

    /// <summary>
    /// Puts the picked breakwater back as it stood when picked.
    /// </summary>
    internal void RevertPicked() {
      if (this.snapshot is not {} snapshot) {
        return;
      }

      // A value a slider left waiting is reverted with the rest.
      this.waiting = null;

      var why = this.breakwaters.Revert(snapshot);

      this.Settle(why, snapshot.Record);

      // Put back as picked, the breakwater has nothing left to revert until its next edit, and its
      // stones are a style's as a pick reads them.
      if (why is null) {
        this.snapshot = null;
        this.ReadStyleStones();
      }

      this.PlaySound(static sounds => sounds.m_PolygonToolRemovePointSound);
    }

    /// <summary>
    /// Deletes the picked breakwater and its stones.
    /// </summary>
    internal void DeletePicked() {
      if (this.gestures.Picked is not {} picked) {
        return;
      }

      this.waiting = null;
      this.breakwaters.Delete(picked);
      this.Unpick();
      this.PlaySound(static sounds => sounds.m_PolygonToolDeleteAreaSound);
    }

    protected override void OnCreate() {
      base.OnCreate();

      this.breakwaters = this.World.GetOrCreateSystemManaged<BreakwaterSystem>();
      this.terrainSystem = this.World.GetOrCreateSystemManaged<TerrainSystem>();
      this.waterSystem = this.World.GetOrCreateSystemManaged<WaterSystem>();
      this.soundQuery = this.GetEntityQuery(ComponentType.ReadOnly<ToolUXSoundSettingsData>());

      this.OnCreateHints();
      this.OnCreateOverlay();
      this.OnCreateStones();

      // The key binding works wherever a tool can be used, the tool active or not.
      this.toggleAction = Mod.Settings.GetAction(nameof(Settings.KeyBindingToggleTool));
      this.toggleAction.shouldBeEnabled = true;
      this.toggleAction.onInteraction += this.OnToggleInteraction;
    }

    protected override void OnDestroy() {
      this.toggleAction.onInteraction -= this.OnToggleInteraction;

      base.OnDestroy();
    }

    /// <summary>
    /// The tool's item in the Landscaping menu while the menu entered the tool, which keeps it
    /// selected there; null otherwise, as the toolbar opens the menu of the prefab a tool returns.
    /// </summary>
    public override PrefabBase? GetPrefab() =>
      this.offered ?? (this.isEnteredFromMenu ? this.Prefab : null);

    public override bool TrySetPrefab(PrefabBase prefab) {
      if (prefab is BreakwaterToolPrefab) {
        this.isEnteredFromMenu = true;
        this.offered = null;

        return true;
      }

      return this.IsActive && this.TakeOffer(prefab);
    }

    public override void InitializeRaycast() {
      base.InitializeRaycast();

      // The terrain alone, under water too, as the vanilla terrain tools: a breakwater lies on it.
      this.m_ToolRaycastSystem.typeMask = TypeMask.Terrain;

      // A breakwater's toe can lie past the playable area's edge, as a map's coast often does.
      this.m_ToolRaycastSystem.raycastFlags |= RaycastFlags.Outside;
    }

    protected override void OnStartRunning() {
      base.OnStartRunning();

      this.Unpick();
      this.outlinesRevision = uint.MaxValue;

      // A reset of the mod's settings empties the look kept, which the tool then reads again in
      // place of keeping its own.
      if (Mod.Settings.LastLook is "") {
        this.isLookLoaded = false;
      }

      this.LoadLook();
    }

    protected override void OnStopRunning() {
      // An outline not closed is no breakwater, and a vertex held is let go where it was.
      this.Unpick();
      this.isEnteredFromMenu = false;
      this.offered = null;
      this.SaveLook();

      base.OnStopRunning();
    }

    /// <summary>
    /// Runs a frame of the Debug build's scripted drag, which times a slider's re-lays.
    /// </summary>
    partial void OnBenchFrame();

    protected override JobHandle OnUpdate(JobHandle inputDeps) {
      this.OnBenchFrame();

      // The tool makes no temporary entities of its own, so it clears every frame what the tool
      // before it left, as the hovered object's outline: only the active tool's mode runs a clear.
      this.applyMode = ApplyMode.Clear;

      this.heights = this.terrainSystem.GetHeightData(waitForPending: true);
      this.ReadOutlines();

      // A breakwater deleted by another way than the tool's leaves the pick.
      if (this.gestures.Picked is {} picked && !this.breakwaters.IsBreakwater(picked)) {
        this.waiting = null;
        this.Unpick();
      }

      this.LayChoice();

      this.view = View.Of(Camera.main);
      this.CullOutlines();
      this.ReadToeMarkers();
      this.pointer = this.ReadPointer();

      if (!this.m_FocusChanged) {
        this.Interpret();
      }

      // After the gestures, so that a drag's release lays the drag, the waiting value with it: a
      // lay before would put back the outline the record holds.
      this.LayRested();

      this.JudgeGround();
      this.WatchGround();

      var target = this.gestures.Aim(this.pointer);

      this.Hint = this.gestures.Hint(target);
      this.ShowHints(target);
      this.DrawOverlay(target);

      return inputDeps;
    }

    /// <summary>
    /// Feeds the interpreter the frame's input, and applies what it returns.
    /// </summary>
    private void Interpret() {
      var isOverUI = (this.m_ToolRaycastSystem.raycastFlags &
          (RaycastFlags.DebugDisable | RaycastFlags.UIDisable)) !=
        0;

      if (this.gestures.Held >= 0) {
        // The secondary button and the cancel action both drop a drag, as in the area tool.
        if (this.cancelAction.WasPressedThisFrame() ||
          this.secondaryApplyAction.WasPressedThisFrame()) {
          this.Apply(this.gestures.Secondary(this.pointer));
        }
        else if (!this.applyAction.IsPressed() || this.applyAction.WasReleasedThisFrame()) {
          this.Apply(this.gestures.Release(this.pointer));
        }
        else {
          this.Apply(this.gestures.Move(this.pointer));
        }

        return;
      }

      if (isOverUI) {
        return;
      }

      if (this.applyAction.WasPressedThisFrame()) {
        this.Apply(this.gestures.Press(this.pointer));
      }
      else if (this.secondaryApplyAction.WasPressedThisFrame()) {
        this.Apply(this.gestures.Secondary(this.pointer));
      }
    }

    /// <summary>
    /// Does what a gesture asks: an operation, a sound, a pick.
    /// </summary>
    private void Apply(Gesture<Entity> gesture) {
      if (gesture.Kind is not (GestureKind.None or GestureKind.Moved)) {
        this.Refusal = null;
      }

      // A gesture that left the pick, as a point placed, or picked another breakwater by a vertex
      // or an edge, leaves the snapshot of the breakwater picked before.
      if (this.snapshot is {} kept && kept.Record != this.gestures.Picked) {
        this.snapshot = null;
      }

      switch (gesture.Kind) {
        case GestureKind.PointAdded:
          this.PlaySound(static sounds => sounds.m_PolygonToolDropPointSound);

          break;

        case GestureKind.PointRemoved:
          this.PlaySound(static sounds => sounds.m_PolygonToolRemovePointSound);

          break;

        case GestureKind.Closed when gesture.Polygon is {} polygon: this.Create(polygon); break;

        case GestureKind.Picked when gesture.Breakwater is {} breakwater:
          this.Pick(breakwater);
          this.PlaySound(static sounds => sounds.m_SelectEntitySound);

          break;

        case GestureKind.Unpicked: this.Unpick(); break;

        case GestureKind.Grabbed or GestureKind.Inserted:
          // A press on another breakwater's vertex or edge picks it as it grabs.
          if (gesture.Breakwater is {} grabbed) {
            this.TakePick(grabbed);
          }

          this.PlaySound(static sounds => sounds.m_PolygonToolSelectPointSound);

          break;

        case GestureKind.Reshaped
          when gesture is { Breakwater: {} breakwater, Polygon: {} polygon }:
          this.Reshape(breakwater, polygon, gesture.Toe);

          break;

        case GestureKind.ToeToggled when gesture is { Breakwater: {} breakwater, Toe: {} edges }:
          this.EditPicked(breakwater, new BreakwaterChange(Toe: edges));
          this.PlaySound(static sounds => sounds.m_PolygonToolSelectPointSound);

          break;

        case GestureKind.Dropped:
          this.PlaySound(static sounds => sounds.m_PolygonToolDropPointSound);

          break;

        case GestureKind.Refused:
          this.Refusal = LocalizedString.Id($"BreakwaterTool.Tool.REFUSED[{gesture.Refusal}]");
          this.PlaySound(static sounds => sounds.m_PlaceBuildingFailSound);

          break;
      }
    }

    private void Create(float2[] polygon) {
      var watch = Stopwatch.StartNew();
      var why = this.breakwaters.Create(
        new BreakwaterState(
          polygon,
          [.. this.choice],
          this.Parameters,
          StoneProtectionKind.Anarchy
        ),
        out var record
      );

      if (why is not null) {
        // The outline stays drawn, so the player can change it and close it again.
        this.Refusal = this.Explain(why);
        this.PlaySound(static sounds => sounds.m_PlaceBuildingFailSound);

        return;
      }

      this.layoutCosts[record] = watch.Elapsed.TotalMilliseconds;
      this.gestures.Edit(record, polygon);
      this.snapshot = null;
      this.PlaySound(static sounds => sounds.m_PolygonToolFinishAreaSound);
    }

    private void Pick(Entity record) {
      if (this.TakePick(record) is {} state) {
        this.gestures.Edit(record, state.Polygon, state.Toe);
      }
    }

    /// <summary>
    /// Takes a breakwater the gestures or the panel picked: lays what a slider left waiting on the
    /// one before, and shows its look, which a new breakwater starts from.
    /// </summary>
    /// <returns>What the breakwater's record holds, or null when it is none.</returns>
    private BreakwaterState? TakePick(Entity record) {
      this.LayWaiting();

      if (this.breakwaters.Read(record) is not {} state) {
        return null;
      }

      this.snapshot = null;
      this.ShowLook(state);
      this.ReadStyleStones();
      this.isOutdatedDue = true;

      return state;
    }

    private void Unpick() {
      this.LayWaiting();
      this.gestures.Reset();
      this.snapshot = null;
      this.Refusal = null;
      this.IsOutdated = false;
      this.LookRevision++;
    }

    private void Reshape(Entity record, float2[] polygon, bool[]? toeEdges) {
      // No toe edge left by hand hands the toe back to the layout.
      this.EditPicked(record, new BreakwaterChange(polygon, Toe: toeEdges ?? []));

      // The drop sound whether the edit lays or not, as the vanilla area tool plays it on every
      // vertex dropped (decompile: AreaToolSystem, State.Modify).
      this.PlaySound(static sounds => sounds.m_PolygonToolDropPointSound);
    }

    /// <summary>
    /// Edits the picked breakwater; the first edit since the pick that lays it keeps the breakwater
    /// as it stood before, for Revert.
    /// </summary>
    /// <returns>Why the edit laid nothing, or null.</returns>
    private string? EditPicked(Entity record, BreakwaterChange change) {
      change = this.TakeWaiting(record, change);

      var before = this.snapshot ?? this.breakwaters.Snapshot(record);
      var watch = Stopwatch.StartNew();
      var why = this.breakwaters.Edit(record, change);
      var cost = watch.Elapsed.TotalMilliseconds;

      this.Settle(why, record);

      // A refused edit lays nothing, so its time says nothing of the layout's.
      if (why is null) {
        this.layoutCosts[record] = cost;
        this.snapshot = before;
      }

      return why;
    }

    /// <summary>
    /// Shows what an edit gave: the outline the record now holds, and why it laid nothing.
    /// </summary>
    private void Settle(string? why, Entity record) {
      // A bare null would convert to an empty LocalizedString through its conversion from a string,
      // and show as an empty refusal.
      this.Refusal = why is null ? (LocalizedString?) null : this.Explain(why);

      // The panel shows what the record holds, whatever the edit asked for.
      if (this.breakwaters.Read(record) is {} state) {
        this.gestures.Edit(record, state.Polygon, state.Toe);
        this.ShowLook(state);
      }

      this.isOutdatedDue = true;
    }

    /// <summary>
    /// Why an operation laid nothing, in the player's words: the layout's verdicts have their own,
    /// and any other reason is quoted.
    /// </summary>
    private LocalizedString Explain(string why) {
      if (this.breakwaters.LastRefusal is not StoneRefusal.None and var refusal) {
        return LocalizedString.Id($"BreakwaterTool.Tool.NOTHING_LAID[{refusal}]");
      }

      if (this.breakwaters.LastLayout is { Verdict: not LayoutVerdict.Laid } layout) {
        return LocalizedString.Id(
          $"BreakwaterTool.Tool.NOTHING_LAID[{layout.Verdict}]",
          ("LIMIT", new LocalizedNumber<int>(BreakwaterLayout.Guard))
        );
      }

      return LocalizedString.Id(
        "BreakwaterTool.Tool.NOTHING_LAID",
        ("REASON", LocalizedString.Value(why))
      );
    }

    /// <summary>
    /// Judges the toe of the outline drawn or picked against the ground as it is this frame, as the
    /// pointer moves its points.
    /// </summary>
    private void JudgeGround() {
      var points = this.gestures.Points;

      if (points.Count < 3) {
        this.toe = [];
        this.Ground = GroundVerdict.Shaped;

        return;
      }

      this.toe = this.gestures.Toe is {} byHand ? [.. byHand] : this.ChooseToe(points);

      // An outline still drawn is no breakwater yet: its ground is judged once it is closed.
      if (this.gestures.Mode is not GestureMode.Editing) {
        this.Ground = GroundVerdict.Shaped;
      }
    }

    /// <summary>
    /// Judges whether the ground under the picked breakwater holds a mound out of the water.
    /// </summary>
    private void JudgePickedGround() {
      var points = this.gestures.Points;
      var water = this.waterSystem.GetSurfaceData(out var deps);

      deps.Complete();

      this.Ground = GroundCheck.Judge(
        points,
        this.HeightAt,
        xz => WaterUtils.SampleDepth(ref water, new float3(xz.x, 0f, xz.y))
      );
    }

    /// <summary>
    /// Where the pointer meets the terrain, how far a node or an edge reaches there, and the
    /// breakwater under it.
    /// </summary>
    private Pointer<Entity> ReadPointer() {
      if (!this.GetRaycastResult(out Entity _, out var hit)) {
        this.outlines.Clear();

        return new Pointer<Entity>(null, 0f, null, this.view.Position);
      }

      var at = hit.m_HitPosition.xz;
      var reach = this.view.ReachAt(hit.m_HitPosition);

      this.ReachOutlines(hit.m_HitPosition, reach);

      return new Pointer<Entity>(
        at,
        reach,
        this.BreakwaterAt(at),
        this.view.Position,
        ToeMarkers.Aimed(this.toeMarkers, this.view.Position, hit.m_HitPosition)
      );
    }

    /// <summary>
    /// The toe edges the layout chooses for an outline on the ground as it is this frame.
    /// </summary>
    private bool[] ChooseToe(IReadOnlyList<float2> outline) =>
      BreakwaterLayout.ToeEdges(outline, new GameLayoutHost(this.heights), this.Parameters);

    private float HeightAt(float2 xz) =>
      TerrainUtils.SampleHeight(ref this.heights, new float3(xz.x, 0f, xz.y));

    /// <summary>
    /// The breakwater whose outline holds a point, the smallest when outlines overlap, or null.
    /// </summary>
    private Entity? BreakwaterAt(float2 at) {
      var holding = this.outlines
        .Where(outline => GroundCheck.Contains(outline.Outline, at))
        .OrderBy(static outline => BreakwaterLayout.Area(outline.Outline))
        .Select(static outline => (Entity?) outline.Record);

      return holding.FirstOrDefault();
    }

    private void ReadOutlines() {
      if (this.outlinesRevision == this.breakwaters.Revision) {
        return;
      }

      this.outlinesRevision = this.breakwaters.Revision;
      this.city.Clear();
      this.toes.Clear();

      foreach (var record in this.breakwaters.List()) {
        if (this.breakwaters.Read(record) is not {} state) {
          continue;
        }

        this.city.Add((record, state.Polygon, this.BoxOf(state.Polygon)));

        if (state.Toe is {} byHand) {
          this.toes[record] = byHand;
        }
      }
    }

    /// <summary>
    /// The box an outline lies in, on the ground as it now is, with room above and below for the
    /// ground to move before the outline is read again.
    /// </summary>
    private Bounds BoxOf(float2[] outline) {
      var heights = outline.Select(this.HeightAt).ToArray();
      var low = outline.Aggregate(math.min);
      var high = outline.Aggregate(math.max);
      var box = new Bounds();

      box.SetMinMax(
        new Vector3(low.x, heights.Min() - BreakwaterToolSystem.BoxMargin, low.y),
        new Vector3(high.x, heights.Max() + BreakwaterToolSystem.BoxMargin, high.y)
      );

      return box;
    }

    /// <summary>
    /// Keeps the breakwaters whose box the camera sees: one out of view is neither under the
    /// pointer nor drawn.
    /// </summary>
    // [PERF] The overlay draws the outlines in view, in place of every outline of the city at each
    // frame.
    private void CullOutlines() {
      this.inView.Clear();

      var camera = Camera.main;

      // Unity's == also sees a destroyed camera, which `is null` misses.
      if (camera == null) {
        return;
      }

      GeometryUtility.CalculateFrustumPlanes(camera, this.frustum);

      foreach (var outline in this.city) {
        if (GeometryUtility.TestPlanesAABB(this.frustum, outline.Box)) {
          this.inView.Add(outline);
        }
      }
    }

    /// <summary>
    /// Keeps, of the breakwaters in view, those whose box the pointer's ray passes within its reach
    /// of, before it meets the ground: the only ones a node or an edge of which it can aim at.
    /// </summary>
    // [PERF] The gestures weigh the edges of the outlines at the pointer, in place of every edge
    // in view at each frame.
    private void ReachOutlines(float3 ground, float reach) {
      this.outlines.Clear();

      var toGround = math.distance(this.view.Position, ground);

      if (toGround <= 0f) {
        return;
      }

      var ray = new Ray(this.view.Position, (ground - this.view.Position) / toGround);

      foreach (var (record, outline, box) in this.inView) {
        var reached = box;

        reached.Expand(2f * reach);

        if (reached.IntersectRay(ray, out var distance) && distance <= toGround + reach) {
          this.outlines.Add((record, outline));
        }
      }
    }

    private void OnToggleInteraction(ProxyAction action, InputActionPhase phase) {
      // The game blocks its own tool keys outside a city or the editor and while one loads, not the
      // mod's: the main menu or a loading screen would activate the tool (decompile:
      // ToolSystem.OnUpdate).
      var game = GameManager.instance;

      if (phase is InputActionPhase.Performed &&
        game.gameMode.IsGameOrEditor() &&
        !game.isGameLoading) {
        this.Toggle();
      }
    }

    private void PlaySound(Func<ToolUXSoundSettingsData, Entity> pick) {
      if (this.soundQuery.TryGetSingleton<ToolUXSoundSettingsData>(out var sounds)) {
        AudioManager.instance.PlayUISound(pick(sounds));
      }
    }
  }
}
