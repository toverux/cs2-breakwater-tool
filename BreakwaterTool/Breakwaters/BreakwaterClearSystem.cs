using Game;
using Unity.Entities;

namespace BreakwaterTool.Breakwaters {
  /// <summary>
  /// Destroys every breakwater record when the game loads, beside the game's own
  /// <c>ClearSystem</c>, which clears only entities that carry one of the components it lists.
  /// Without it, the records of the city left behind would join the loaded ones at every load.
  /// </summary>
  internal sealed partial class BreakwaterClearSystem : GameSystemBase {
    private EntityQuery recordsQuery;

    protected override void OnCreate() {
      base.OnCreate();

      this.recordsQuery = this.GetEntityQuery(ComponentType.ReadOnly<BreakwaterRecord>());
    }

    protected override void OnUpdate() {
      this.EntityManager.DestroyEntity(this.recordsQuery);
    }
  }
}
