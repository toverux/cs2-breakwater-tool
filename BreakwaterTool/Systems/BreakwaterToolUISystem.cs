using System;
using System.Linq;
using BreakwaterTool.Breakwaters;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using Game;
using Game.Prefabs;
using Game.SceneFlow;
using Game.UI;
using Game.UI.Editor;
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

    // Created in OnCreate, which the game runs before any other member: hence mutable, and null!
    // until then.
    private BreakwaterToolSystem tool = null!;

    private BreakwaterSystem breakwaters = null!;

    private PrefabSystem prefabSystem = null!;

    private bool wasActive;

    protected override void OnCreate() {
      base.OnCreate();

      try {
        this.tool = this.World.GetOrCreateSystemManaged<BreakwaterToolSystem>();
        this.breakwaters = this.World.GetOrCreateSystemManaged<BreakwaterSystem>();
        this.prefabSystem = this.World.GetOrCreateSystemManaged<PrefabSystem>();

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

        this.AddBinding(
          new TriggerBinding(BreakwaterToolUISystem.BindingGroup, "toggleTool", this.tool.Toggle)
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
      }

      this.wasActive = isActive;

      base.OnUpdate();
    }

    protected override void OnGamePreload(Purpose purpose, GameMode mode) {
      base.OnGamePreload(purpose, mode);

      try {
        // The prefabs exist from the first load on, after every mod's OnLoad.
        this.tool.Prefab ??= this.CreateLandscapingItem();

        this.tool.EditorButton = mode is GameMode.Editor ? this.AddEditorTool() : null;
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
