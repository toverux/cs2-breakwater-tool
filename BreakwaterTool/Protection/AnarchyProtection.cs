using System;
using Colossal.Entities;
using Colossal.IO.AssetDatabase;
using Game.Objects;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Transform = Game.Objects.Transform;

namespace BreakwaterTool.Protection;

/// <summary>
/// Keeps a breakwater's stones visible and in place against the game through Anarchy's bridge: its
/// override prevention keeps visible the stones the game would hide where their neighbours overlap
/// them, and its transform lock puts them back where they were laid after a terrain change.
/// Everything that stamps a stone's protection goes through here.
/// The bridge is found the first time it is needed, as Anarchy may load after this mod, and again
/// after the player changes the mod set.
/// </summary>
internal sealed class AnarchyProtection {
  private AnarchyBridgeLink? link;

  private string? unavailable;

  // Set by the mod set's event, which the game may raise off the main thread.
  private volatile bool isStale = true;

  internal AnarchyProtection() {
    if (AssetDatabase<ParadoxMods>.instance.dataSource is ParadoxModsDataSource mods) {
      mods.onAfterActivePlaysetOrModStatusChanged += () => this.isStale = true;
    }
  }

  /// <summary>
  /// Why stones cannot be protected now, in plain words, or null when they can.
  /// Nothing is laid while it is not null: never unprotected stones.
  /// </summary>
  internal string? Unavailable {
    get {
      this.Refresh();

      return this.unavailable;
    }
  }

  /// <summary>
  /// The bridge, or null when <see cref="Unavailable"/> says why not.
  /// </summary>
  internal AnarchyBridgeLink? Link {
    get {
      this.Refresh();

      return this.link;
    }
  }

  /// <summary>
  /// What laying stones of a prefab takes.
  /// </summary>
  internal PrefabCare Classify(EntityManager entityManager, Entity prefab) {
    if (!entityManager.TryGetComponent<ObjectGeometryData>(prefab, out var geometry) ||
      !StoneExposure.IsExposed(geometry.m_Flags)) {
      return PrefabCare.Inert;
    }

    // The bridge's own conditions: an overridable static object, no building, and for the lock no
    // net or utility object.
    var isAccepted = (geometry.m_Flags & GeometryFlags.Overridable) != 0 &&
      !entityManager.HasComponent<BuildingData>(prefab) &&
      !entityManager.HasComponent<NetObjectData>(prefab) &&
      !entityManager.HasComponent<UtilityObjectData>(prefab);

    return isAccepted ? PrefabCare.Protect : PrefabCare.Unprotectable;
  }

  /// <summary>
  /// Protects stones of one prefab the protection covers, their transforms already set.
  /// </summary>
  /// <returns>How many stones the protection skipped: any is a failure.</returns>
  internal int Protect(EntityManager entityManager, NativeArray<Entity> stones) {
    if (stones.Length == 0) {
      return 0;
    }

    var bridge = this.Link ??
      throw new InvalidOperationException($"Protect called while {this.unavailable}.");

    // Anarchy's own code runs in the bridge: whatever it throws skips every stone, so none is laid
    // unprotected.
    try {
      // The bridge judges a stone by its prefab: asked once, its component then goes on the
      // prefab's other stones in bulk.
      // Its own array form, AddAnarchyComponent(NativeArray<Entity>), runs one past the end of the
      // array (reported upstream). Once fixed, it can replace this bulk add: it checks every stone
      // itself but returns nothing, so count as skipped the stones left without
      // OverridePreventionType after it.
      if (!bridge.TryAddOverridePrevention(stones[0])) {
        return stones.Length;
      }

      entityManager.AddComponent(
        stones.GetSubArray(1, stones.Length - 1),
        bridge.OverridePreventionType
      );

      // The lock records each stone's transform, so it goes one stone at a time.
      var skipped = 0;

      foreach (var stone in stones) {
        if (!bridge.TryAddTransformLock(stone, entityManager.GetComponentData<Transform>(stone))) {
          skipped++;
        }
      }

      return skipped;
    }
    catch (Exception ex) {
      Mod.Log.Error($"{nameof(AnarchyProtection)}: Anarchy {bridge.Version}'s bridge threw: {ex}");

      return stones.Length;
    }
  }

  private void Refresh() {
    if (!this.isStale) {
      return;
    }

    this.isStale = false;
    this.unavailable = AnarchyBridgeLink.TryResolve(out this.link);

    if (this.link is not null) {
      Mod.Log.Info($"{nameof(AnarchyProtection)}: Anarchy {this.link.Version} bridge linked.");
    }
    else {
      Mod.Log.Warn($"{nameof(AnarchyProtection)}: {this.unavailable}, breakwaters cannot be laid.");
    }
  }
}
