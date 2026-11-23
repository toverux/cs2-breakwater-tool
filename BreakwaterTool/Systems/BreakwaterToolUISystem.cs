using System;
using System.Linq;
using BreakwaterTool.Breakwaters;
using BreakwaterTool.Utils.Writers;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using Game;
using Game.Prefabs;
using Game.SceneFlow;
using Game.UI;
using Game.UI.Editor;
using Unity.Entities;
using UnityEngine;

namespace BreakwaterTool.Systems {
  /// <summary>
  /// The tool's entries and panel: its item in the Landscaping menu and its button in the editor's
  /// toolbar, and the bindings of its tool options sections.
  /// Its bindings update only while the tool is active, and once as it is left.
  /// In the editor, the toolbar's button and the tool's editor panel follow the tool however it is
  /// entered or left.
  /// </summary>
  internal sealed partial class BreakwaterToolUISystem : UISystemBase {
    private const string BindingGroup = "breakwaterTool.tool";

    /// <summary>
    /// The assembly name of Find It, whose list adds stones to the active tool.
    /// </summary>
    private const string FindIt = "FindIt";

    /// <summary>
    /// The icon of the tool's button in the editor's toolbar, drawn as the vanilla buttons beside
    /// it; the UI build deploys it from <c>UI/src/icons</c>.
    /// </summary>
    private const string EditorIcon = "coui://ui-mods/images/breakwater-tool-editor.svg";

    /// <summary>
    /// The vanilla category of the terrain tools, in the Landscaping menu.
    /// </summary>
    internal const string LandscapingCategory = "Terraforming";

    /// <summary>
    /// The icon of the tool's item in the Landscaping menu, drawn as the vanilla items beside it.
    /// </summary>
    private const string LandscapingIcon = "coui://ui-mods/images/breakwater-tool-landscaping.svg";

    /// <summary>
    /// The game's image for what has none.
    /// </summary>
    private const string Placeholder = "Media/Placeholder.svg";

    // Created in OnCreate, which the game runs before any other member: hence mutable, and null!
    // until then.
    private BreakwaterToolSystem tool = null!;

    private BreakwaterSystem breakwaters = null!;

    private PrefabSystem prefabSystem = null!;

    private ImageSystem imageSystem = null!;

    // The look as last written to the panel, and the revision of the tool's it was read at.
    private RawValueBinding lookBinding = null!;

    private LookView? look;

    private uint lookRevision = uint.MaxValue;

    private bool wasActive;

    // The Stones row's category, null until read; read again as the tool turns on or off.
    // [PERF] Read once an activation, in place of every frame: it changes only with the load.
    private Entity? stoneCategory;

    // Whether Find It is loaded, read at each load: every mod's OnLoad has run by then.
    private bool hasFindIt;

    protected override void OnCreate() {
      base.OnCreate();

      try {
        this.tool = this.World.GetOrCreateSystemManaged<BreakwaterToolSystem>();
        this.breakwaters = this.World.GetOrCreateSystemManaged<BreakwaterSystem>();
        this.prefabSystem = this.World.GetOrCreateSystemManaged<PrefabSystem>();
        this.imageSystem = this.World.GetOrCreateSystemManaged<ImageSystem>();

        this.AddUpdateBinding(
          new GetterValueBinding<bool>(
            BreakwaterToolUISystem.BindingGroup,
            "isPicked",
            () => this.tool.Picked != default
          )
        );

        this.AddUpdateBinding(
          new GetterValueBinding<bool>(
            BreakwaterToolUISystem.BindingGroup,
            "canRevert",
            () => this.tool.CanRevert
          )
        );

        this.AddUpdateBinding(
          new GetterValueBinding<string>(
            BreakwaterToolUISystem.BindingGroup,
            "editorOptionOff",
            this.GetEditorOptionOff
          )
        );

        this.AddUpdateBinding(
          new GetterValueBinding<bool>(
            BreakwaterToolUISystem.BindingGroup,
            "isOutdated",
            () => this.tool.IsOutdated
          )
        );

        this.AddUpdateBinding(
          new GetterValueBinding<Entity>(
            BreakwaterToolUISystem.BindingGroup,
            "stoneCategory",
            () => this.stoneCategory ??= this.GetStoneCategory()
          )
        );

        this.AddUpdateBinding(
          new GetterValueBinding<string>(
            BreakwaterToolUISystem.BindingGroup,
            "stonesHint",
            this.GetStonesHint
          )
        );

        this.AddBinding(
          new ValueBinding<string[]>(
            BreakwaterToolUISystem.BindingGroup,
            "styles",
            [.. BreakwaterStyles.Names],
            new ArrayWriter<string>(new StringWriter())
          )
        );

        var lookWriter = new LookWriter();

        this.lookBinding = new RawValueBinding(
          BreakwaterToolUISystem.BindingGroup,
          "look",
          writer => lookWriter.Write(writer, this.ReadLook())
        );

        this.AddBinding(this.lookBinding);

        this.AddBinding(
          new TriggerBinding(BreakwaterToolUISystem.BindingGroup, "toggleTool", this.tool.Toggle)
        );

        this.AddBinding(
          new TriggerBinding<string, float>(
            BreakwaterToolUISystem.BindingGroup,
            "setParameter",
            this.tool.SetParameter
          )
        );

        this.AddBinding(
          new TriggerBinding<string>(
            BreakwaterToolUISystem.BindingGroup,
            "applyStyle",
            this.tool.ApplyStyle
          )
        );

        this.AddBinding(
          new TriggerBinding<Entity>(
            BreakwaterToolUISystem.BindingGroup,
            "removeStone",
            this.tool.RemoveStone
          )
        );

        this.AddBinding(
          new TriggerBinding(BreakwaterToolUISystem.BindingGroup, "reshuffle", this.tool.Reshuffle)
        );

        this.AddBinding(
          new TriggerBinding(
            BreakwaterToolUISystem.BindingGroup,
            "expectOffer",
            this.tool.ExpectOffer
          )
        );

        this.AddBinding(
          new TriggerBinding(
            BreakwaterToolUISystem.BindingGroup,
            "clearVegetation",
            this.tool.ClearPickedVegetation
          )
        );

        this.AddBinding(
          new TriggerBinding(BreakwaterToolUISystem.BindingGroup, "fit", this.tool.FitPicked)
        );

        this.AddBinding(
          new TriggerBinding(BreakwaterToolUISystem.BindingGroup, "revert", this.tool.RevertPicked)
        );

        this.AddBinding(
          new TriggerBinding(BreakwaterToolUISystem.BindingGroup, "delete", this.tool.DeletePicked)
        );
      }
      catch (Exception ex) {
        Mod.Log.ErrorFatal(ex);
      }
    }

    protected override void OnUpdate() {
      var isActive = this.tool.IsActive;

      // Idle, the tool costs nothing: its bindings change only while it is active.
      if (!isActive && !this.wasActive) {
        return;
      }

      if (isActive != this.wasActive) {
        this.FollowInEditorToolbar(isActive);
        this.stoneCategory = null;
      }

      this.wasActive = isActive;

      // [PERF] The look is written when it changes, in place of a comparison at every frame.
      if (this.lookRevision != this.tool.LookRevision) {
        this.look = null;
        this.lookBinding.Update();
      }

      base.OnUpdate();
    }

    protected override void OnGamePreload(Purpose purpose, GameMode mode) {
      base.OnGamePreload(purpose, mode);

      try {
        // The prefabs exist from the first load on, after every mod's OnLoad.
        this.tool.Prefab ??= this.CreateLandscapingItem();

        this.tool.EditorButton = mode is GameMode.Editor ? this.AddEditorTool() : null;

        this.hasFindIt = AppDomain.CurrentDomain.GetAssemblies()
          .Any(static assembly => assembly.GetName().Name == BreakwaterToolUISystem.FindIt);
      }
      catch (Exception ex) {
        Mod.Log.ErrorRecoverable(ex);
      }
    }

    /// <summary>
    /// The tool's item among the terrain tools of the Landscaping menu: a prefab of the mod's own
    /// that no vanilla tool claims, so a click on it activates this tool.
    /// No entity of a city refers to it, so a save never names it.
    /// </summary>
    private BreakwaterToolPrefab? CreateLandscapingItem() {
      if (!this.prefabSystem.TryGetPrefab(
          new PrefabID(nameof(UIAssetCategoryPrefab), BreakwaterToolUISystem.LandscapingCategory),
          out var category
        ) ||
        category is not UIAssetCategoryPrefab terrainTools) {
        Mod.Log.Warn(
          $"{nameof(BreakwaterToolUISystem)}: The Landscaping menu has no " +
          $"{BreakwaterToolUISystem.LandscapingCategory} category: the tool has no item there."
        );

        return null;
      }

      var prefab = ScriptableObject.CreateInstance<BreakwaterToolPrefab>();

      prefab.name = BreakwaterToolSystem.Id;

      var item = ScriptableObject.CreateInstance<UIObject>();

      item.m_Group = terrainTools;
      item.m_Icon = BreakwaterToolUISystem.LandscapingIcon;
      item.m_Priority = int.MaxValue;
      item.active = true;
      item.name = nameof(UIObject);

      prefab.AddComponentFrom(item);

      this.prefabSystem.AddPrefab(prefab);

      return prefab;
    }

    /// <summary>
    /// The tool's button in the editor's toolbar, after the vanilla ones, added once; it opens the
    /// tool's editor panel with the tool.
    /// </summary>
    private EditorTool AddEditorTool() {
      var toolbar = this.World.GetOrCreateSystemManaged<EditorToolUISystem>();

      if (toolbar.tools.OfType<EditorTool>()
          .FirstOrDefault(static tool =>
            tool.id == BreakwaterToolSystem.Id
          ) is {} added) {
        return added;
      }

      var editorTool = new EditorTool(this.World) {
        id = BreakwaterToolSystem.Id,
        icon = BreakwaterToolUISystem.EditorIcon,
        tool = this.tool,
        panel = this.World.GetOrCreateSystemManaged<BreakwaterToolPanelSystem>()
      };

      toolbar.tools = [.. toolbar.tools, editorTool];

      return editorTool;
    }

    /// <summary>
    /// Selects the tool's button in the editor's toolbar as the tool is entered another way than
    /// the toolbar, which opens its editor panel; and unselects it as the tool is left another way,
    /// which closes the panel.
    /// Closing the panel leaves the tool, through the toolbar.
    /// </summary>
    private void FollowInEditorToolbar(bool isActive) {
      if (this.tool.EditorButton is not {} editorTool) {
        return;
      }

      var toolbar = this.World.GetOrCreateSystemManaged<EditorToolUISystem>();

      if (isActive && toolbar.activeTool != editorTool) {
        toolbar.activeTool = editorTool;
      }
      else if (!isActive && toolbar.activeTool == editorTool) {
        toolbar.activeTool = null;
      }
    }

    /// <summary>
    /// The look the panel shows, read from the tool once per revision of it.
    /// </summary>
    private LookView ReadLook() {
      if (this.look is {} current && this.lookRevision == this.tool.LookRevision) {
        return current;
      }

      this.lookRevision = this.tool.LookRevision;

      var stones = this.tool.JudgeStones();

      return this.look = new LookView(
        this.tool.Parameters,
        stones,
        [
          .. stones.Select(stone =>
            this.prefabSystem.TryGetPrefab<PrefabBase>(stone.Prefab, out var prefab)
              ? ImageSystem.GetThumbnail(prefab) ?? ""
              : ""
          )
        ],
        // The icon of the prefab's category in the asset menu, or the game's placeholder.
        [
          .. stones.Select(stone =>
            this.imageSystem.GetGroupIcon(stone.Prefab) ?? BreakwaterToolUISystem.Placeholder
          )
        ],
        this.tool.Style ?? ""
      );
    }

    /// <summary>
    /// The asset menu's category of stones, the one the default style's stones show in, which the
    /// Stones row opens; <see cref="Entity.Null"/> in the editor, which has no asset menu, and
    /// where the game lacks it.
    /// </summary>
    private Entity GetStoneCategory() =>
      GameManager.instance.gameMode is GameMode.Game &&
      this.breakwaters.Resolve(BreakwaterStyles.DefaultStyle.Stones)
        .FirstOrDefault() is var stone &&
      this.EntityManager.TryGetComponent<UIObjectData>(stone, out var item)
        ? item.m_Group
        : Entity.Null;

    /// <summary>
    /// How stones are added where the asset menu cannot: in the editor, through Find It, or not at
    /// all without it; empty in a game.
    /// </summary>
    private string GetStonesHint() =>
      GameManager.instance.gameMode is not GameMode.Editor
        ? ""
        : this.hasFindIt
          ? "FindIt"
          : "NoFindIt";

    /// <summary>
    /// The locale id of Anarchy's editor option when the stones lie unprotected for it being off,
    /// in the editor; empty otherwise.
    /// </summary>
    private string GetEditorOptionOff() {
      if (GameManager.instance.gameMode is not GameMode.Editor ||
        this.breakwaters.Anarchy is not {} anarchy ||
        anarchy.PreventOverrideInEditor is not false) {
        return "";
      }

      // Anarchy's own label names the option in the player's language; the mod's copy of its
      // English label stands in when it cannot be read.
      return anarchy.EditorOptionLabelId ?? "BreakwaterTool.Tool.ANARCHY_EDITOR_OPTION";
    }
  }
}
