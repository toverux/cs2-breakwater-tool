using System.Collections.Generic;
using System.Linq;
using Colossal.Entities;
using Game.Prefabs;
using Game.UI.Localization;
using Unity.Entities;

namespace BreakwaterTool.Breakwaters {
  /// <summary>
  /// The stones the tool lays, and the two ways the game's own lists add to them while the tool is
  /// active: a click on an object in the asset menu, and Find It's multi-select.
  /// </summary>
  internal sealed partial class BreakwaterToolSystem {
    // Created in OnCreate.
    private PrefabSystem prefabSystem = null!;

    // The stone the game's asset menu offered last, which the tool gives as its own prefab while it
    // is active: the menu shows the category of the active tool's prefab, and would leave the
    // stones' category for the tool's item otherwise.
    // Every offer kept becomes it: the toolbar clears its menu on any frame the active tool, other
    // than the default one, gives no prefab (read in the decompile's ToolbarUISystem.OnUpdate).
    private PrefabBase? offered;

    // Whether the Stones row just asked the asset menu for a category, whose asset the menu then
    // offers at once.
    private bool isOfferExpected;

    // Whether Find It changed the stones since the tool's last update, which lays them.
    private bool isChoiceDue;

    /// <summary>
    /// Find It's multi-select contract, which it finds by reflection on the active tool, by these
    /// three names and signatures: with them a Ctrl-click in Find It toggles a prefab among the
    /// stones and a Shift-click sets the stones to a range, in place of leaving the tool for the
    /// prefab's own.
    /// A range comes as a call per prefab in one frame, so each call only changes the stones, and
    /// the tool's next update lays them once.
    /// Read from Find It 2.0 (Paradox Mods 77240), FindItUISystem.MultiSelect.cs.
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    public void SelectPrefab(PrefabBase prefab) {
      if (this.prefabSystem.TryGetEntity(prefab, out var entity) &&
        !this.choice.Contains(entity) &&
        this.breakwaters.CanLay(entity)) {
        this.choice.Add(entity);
        this.isChoiceDue = true;
      }
    }

    /// <inheritdoc cref="SelectPrefab"/>
    // ReSharper disable once UnusedMember.Global
    public void DeselectPrefab(PrefabBase prefab) {
      if (this.prefabSystem.TryGetEntity(prefab, out var entity) && this.choice.Remove(entity)) {
        this.isChoiceDue = true;
      }
    }

    /// <inheritdoc cref="SelectPrefab"/>
    // ReSharper disable once UnusedMember.Global
    public IEnumerable<PrefabBase> GetSelectedPrefabs() =>
      this.choice
        .Select(entity => this.prefabSystem.TryGetPrefab<PrefabBase>(entity, out var prefab)
          ? prefab
          : null
        )
        .OfType<PrefabBase>();

    /// <summary>
    /// Adds a prefab to the stones, when it is one or a random prefab that lists some.
    /// </summary>
    /// <returns>Whether the prefab is among the stones now.</returns>
    internal bool AddStone(Entity prefab) {
      if (this.choice.Contains(prefab)) {
        return true;
      }

      if (!this.breakwaters.CanLay(prefab)) {
        return false;
      }

      this.ChangeLook(new BreakwaterChange(Choice: [.. this.choice, prefab]));

      return true;
    }

    internal void RemoveStone(Entity prefab) {
      if (this.choice.Contains(prefab)) {
        this.ChangeLook(
          new BreakwaterChange(Choice: [.. this.choice.Where(kept => kept != prefab)])
        );
      }
    }

    /// <summary>
    /// Says that the Stones row is about to ask the asset menu for a category: the asset the menu
    /// offers next is only shown selected there, whatever it is.
    /// </summary>
    internal void ExpectOffer() => this.isOfferExpected = true;

    /// <summary>
    /// Lays the stones Find It changed since the last update, once for all its calls; a picked
    /// breakwater that refuses them shows its own again.
    /// </summary>
    private void LayChoice() {
      if (!this.isChoiceDue) {
        return;
      }

      this.isChoiceDue = false;
      this.ChangeLook(new BreakwaterChange(Choice: [.. this.choice]));
    }

    /// <summary>
    /// Takes a prefab the game offers while the tool is active, as the asset menu does, or leaves
    /// it to its own tool, which leaves this one.
    /// A click on an asset of the category the menu shows adds it to the stones when it can be
    /// laid; an object that cannot is refused, the tool kept, and anything else, as a terrain tool,
    /// goes to its own tool.
    /// The menu also offers a category's asset as the player opens the category, the one last
    /// selected there or the first: that one is only shown selected, and a click on it counts. The
    /// tool keeps it when its category is of the menu shown or when the Stones row asked for it, so
    /// that going from a category to another keeps the tool; any other offer, as a plain click in
    /// Find It, goes to its own tool, a stone too, unless no category is shown and it is among the
    /// stones: Find It, reopened, offers its active prefab again, which it keeps among the stones.
    /// </summary>
    private bool TakeOffer(PrefabBase prefab) {
      var isExpected = this.isOfferExpected;

      this.isOfferExpected = false;

      if (!this.prefabSystem.TryGetEntity(prefab, out var entity)) {
        return false;
      }

      var category = this.CategoryOf(prefab);
      var shown = this.CategoryOf(this.GetPrefab());
      var isClick = category != Entity.Null && category == shown;
      var isReoffered = shown == Entity.Null && this.choice.Contains(entity);

      if (!isClick && !isExpected && !isReoffered && !this.IsOfMenuShown(category, shown)) {
        return false;
      }

      if (this.breakwaters.CanLay(entity)) {
        if (isClick) {
          this.AddStone(entity);
        }
      }
      else if (isClick) {
        if (prefab is not ObjectPrefab) {
          return false;
        }

        this.Refusal = LocalizedString.Id("BreakwaterTool.Tool.REFUSED[NotAStone]");
        this.PlaySound(static sounds => sounds.m_PlaceBuildingFailSound);
      }

      this.offered = prefab;

      return true;
    }

    /// <summary>
    /// Whether two asset menu categories are tabs of one menu.
    /// </summary>
    private bool IsOfMenuShown(Entity category, Entity shown) =>
      this.EntityManager.TryGetComponent<UIAssetCategoryData>(category, out var offeredIn) &&
      this.EntityManager.TryGetComponent<UIAssetCategoryData>(shown, out var shownIn) &&
      offeredIn.m_Menu != Entity.Null &&
      offeredIn.m_Menu == shownIn.m_Menu;

    /// <summary>
    /// The asset menu category a prefab shows in, or <see cref="Entity.Null"/>.
    /// </summary>
    private Entity CategoryOf(PrefabBase? prefab) =>
      prefab is not null &&
      this.prefabSystem.TryGetEntity(prefab, out var entity) &&
      this.EntityManager.TryGetComponent<UIObjectData>(entity, out var item)
        ? item.m_Group
        : Entity.Null;

    private void OnCreateStones() {
      this.prefabSystem = this.World.GetOrCreateSystemManaged<PrefabSystem>();

      // The game offers a prefab picked in its asset menu to each tool in turn, and the first that
      // takes it becomes the active one: ahead of the vanilla tools, this one takes a stone while
      // it is active, where the Object tool would take the tool over.
      this.m_ToolSystem.tools.Remove(this);
      this.m_ToolSystem.tools.Insert(0, this);
    }
  }
}
