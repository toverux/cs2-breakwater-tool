using Game;
using Game.Common;
using Game.Prefabs;
using Game.Serialization;
using Unity.Collections;
using Unity.Entities;

namespace BreakwaterTool.Breakwaters {
  /// <summary>
  /// Passes each breakwater's stone prefabs through the game's prefab reference check, as
  /// <c>PrimaryPrefabReferencesSystem</c> does for <c>PrefabRef</c>: on save, the check marks the
  /// prefab so the save keeps it, and on load it remaps the reference to the loaded prefab.
  /// A prefab no stone uses any more would otherwise be left out of the save, and the record would
  /// point at nothing once loaded.
  /// Runs only when the game updates its <see cref="SystemUpdatePhase.PrefabReferences"/> phase, at
  /// a save and at a load.
  /// </summary>
  internal sealed partial class BreakwaterPrefabReferencesSystem : GameSystemBase {
    // Created in OnCreate, which the game runs before any other member.
    private CheckPrefabReferencesSystem checkSystem = null!;

    private EntityQuery recordsQuery;

    protected override void OnCreate() {
      base.OnCreate();

      this.checkSystem = this.World.GetOrCreateSystemManaged<CheckPrefabReferencesSystem>();

      this.recordsQuery = this.GetEntityQuery(
        ComponentType.ReadWrite<BreakwaterStonePrefab>(),
        ComponentType.Exclude<Deleted>()
      );
    }

    protected override void OnUpdate() {
      var references = this.checkSystem.GetPrefabReferences(this, out var dependencies);

      // A handful of records, each with a few prefabs: the main thread does it at once.
      dependencies.Complete();
      this.CompleteDependency();

      using var records = this.recordsQuery.ToEntityArray(Allocator.Temp);

      foreach (var record in records) {
        var prefabs = this.EntityManager.GetBuffer<BreakwaterStonePrefab>(record);

        for (var i = 0; i < prefabs.Length; i++) {
          var element = prefabs[i];

          // The check reads the prefab's data unguarded: a choice that is no prefab, or a prefab
          // removed this session, would fail the save.
          if (!this.EntityManager.HasComponent<PrefabData>(element.prefab)) {
            continue;
          }

          references.Check(ref element.prefab);
          prefabs[i] = element;
        }
      }
    }
  }
}
