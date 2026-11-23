using System.Collections.Generic;
using System.Linq;
using BreakwaterTool.Layout;
using BreakwaterTool.Protection;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BreakwaterTool.Breakwaters {
  /// <summary>
  /// The breakwater operations, the one surface the tool, its UI and the public entry points
  /// share: create, edit, fit to terrain, revert, delete, read and list.
  /// An operation decides at once: it checks its inputs, lays the stones out, applies the guard,
  /// and writes the record.
  /// The stones change at this system's next update, in
  /// <see cref="SystemUpdatePhase.Modification1"/>, where the game itself creates objects:
  /// anywhere after the systems that index new objects, a new stone crashes the game.
  /// The latest operation on a breakwater wins over one still waiting, and stones that update
  /// cannot lay change nothing: the record goes back as it was, and a new breakwater goes.
  /// Nothing lays a breakwater on its own: not a load, not a pick, not a terrain change.
  /// </summary>
  internal sealed partial class BreakwaterSystem : GameSystemBase {
    // What each breakwater's stones become at the next update, and what its record goes back to
    // when they cannot be laid.
    private readonly Dictionary<Entity, PendingLay> pending = [];

    // The plants the next update clears, and those it lays again.
    private readonly HashSet<Entity> plantsToClear = [];

    private readonly List<PlantState> plantsToLay = [];

    // Created in OnCreate, which the game runs before any other member: hence mutable, and null!
    // until then.
    private PrefabSystem prefabSystem = null!;

    private TerrainSystem terrainSystem = null!;

    private StoneCatalog catalog = null!;

    private AnarchyProtection protection = null!;

    private EntityQuery recordsQuery;

    private EntityQuery stonesQuery;

    private EntityQuery plantsQuery;

    /// <summary>
    /// The diagnostics of the layout the last operation ran, laid or not, or null when it refused
    /// before laying out.
    /// </summary>
    internal LayoutResult? LastLayout { get; private set; }

    /// <summary>
    /// The chosen stones the last layout left out because the protection cannot cover them.
    /// </summary>
    internal IReadOnlyList<Entity> LastUnprotectable { get; private set; } = [];

    /// <summary>
    /// Why the last operation refused before laying out, where the reason is the stones chosen.
    /// </summary>
    internal StoneRefusal LastRefusal { get; private set; }

    /// <summary>
    /// Why the last update failed to lay a breakwater's stones, or null when it did not.
    /// </summary>
    internal string? LastFailure { get; private set; }

    /// <summary>
    /// Changes whenever a breakwater is created, changed or deleted, or the city changes: what a
    /// reader of the records compares to know whether to read them again.
    /// </summary>
    internal uint Revision { get; private set; }

    /// <summary>
    /// Anarchy's bridge, or null when it is missing.
    /// </summary>
    internal AnarchyBridgeLink? Anarchy => this.protection.Link;

    /// <summary>
    /// Lays a new breakwater.
    /// </summary>
    /// <param name="record">The breakwater's record, or <see cref="Entity.Null"/> when nothing is
    /// laid.</param>
    /// <returns>Why nothing is laid, or null when the stones appear at the next update.</returns>
    internal string? Create(BreakwaterState state, out Entity record) {
      record = Entity.Null;

      if (this.Plan(ref state, out var stones) is {} why) {
        return why;
      }

      record = this.EntityManager.CreateEntity(
        ComponentType.ReadWrite<BreakwaterRecord>(),
        ComponentType.ReadWrite<BreakwaterVertex>(),
        ComponentType.ReadWrite<BreakwaterStonePrefab>()
      );

      this.Write(record, state);
      this.Queue(record, stones, null);

      return null;
    }

    /// <summary>
    /// Lays a breakwater again with what changed, null keeping what it has; with nothing changed,
    /// it fits the breakwater to the terrain as it now is.
    /// An edit that lays nothing changes nothing.
    /// </summary>
    /// <returns>Why nothing is laid, or null when the stones change at the next update.</returns>
    internal string? Edit(Entity record, BreakwaterChange change) {
      if (this.Read(record) is not {} current) {
        return "it is not a breakwater";
      }

      var state = current with {
        Polygon = change.Polygon?.ToArray() ?? current.Polygon,
        Choice = change.Choice?.ToArray() ?? current.Choice,
        Parameters = change.Parameters ?? current.Parameters,
        Toe = change.Toe is null ? current.Toe : [.. change.Toe]
      };

      if (this.Plan(ref state, out var stones) is {} why) {
        return why;
      }

      this.Write(record, state);
      this.Queue(record, stones, current);

      return null;
    }

    /// <summary>
    /// Deletes a breakwater: its record and the stones linked to it, at the next update.
    /// </summary>
    /// <returns>Why nothing is deleted, or null.</returns>
    internal string? Delete(Entity record) {
      if (!this.IsBreakwater(record)) {
        return "it is not a breakwater";
      }

      this.Queue(record, null, null);
      this.Revision++;

      return null;
    }

    /// <summary>
    /// The breakwater as it stands, its stones included, for <see cref="Revert"/>.
    /// </summary>
    internal BreakwaterSnapshot? Snapshot(Entity record) {
      if (this.Read(record) is not {} state) {
        return null;
      }

      var stones = this.Stones(record)
        .Select(stone => new StoneState(
            this.EntityManager.GetComponentData<PrefabRef>(stone).m_Prefab,
            this.EntityManager.GetComponentData<Transform>(stone),
            this.EntityManager.TryGetComponent<PseudoRandomSeed>(stone, out var seed)
              ? seed.m_Seed
              : (ushort) 0
          )
        )
        .ToArray();

      return new BreakwaterSnapshot(record, state, stones);
    }

    /// <summary>
    /// Puts a breakwater back as a snapshot holds it: its record, and exactly its stones, which
    /// undoes any edit and any hand move or delete since.
    /// </summary>
    /// <returns>
    /// Why nothing is restored, or null when the stones change at the next update.
    /// </returns>
    internal string? Revert(BreakwaterSnapshot snapshot) {
      if (!this.IsBreakwater(snapshot.Record)) {
        return "it is not a breakwater any more";
      }

      if (this.protection.Unavailable is {} unavailable) {
        return unavailable;
      }

      this.Queue(snapshot.Record, snapshot.Stones, this.Read(snapshot.Record));
      this.Write(snapshot.Record, snapshot.State);

      this.plantsToLay.AddRange(snapshot.Cleared);
      snapshot.Cleared.Clear();

      return null;
    }

    /// <summary>
    /// Clears the plants inside a breakwater's outline, trees and bushes, those of a building or a
    /// road excepted, at the next update.
    /// </summary>
    /// <returns>
    /// The plants cleared, as they stood: empty when the entity is no breakwater.
    /// </returns>
    internal PlantState[] ClearVegetation(Entity record) {
      if (this.Read(record) is not {} state) {
        return [];
      }

      var low = state.Polygon.Aggregate(math.min);
      var high = state.Polygon.Aggregate(math.max);

      using var plants = this.plantsQuery.ToEntityArray(Allocator.Temp);
      using var transforms = this.plantsQuery.ToComponentDataArray<Transform>(Allocator.Temp);

      var cleared = new List<PlantState>();

      for (var i = 0; i < plants.Length; i++) {
        var at = transforms[i].m_Position.xz;

        // [PERF] The bounds first, in place of the outline's edges for every plant of the map.
        if (math.any(at < low) ||
          math.any(at > high) ||
          !GroundCheck.Contains(state.Polygon, at) ||
          this.plantsToClear.Contains(plants[i])) {
          continue;
        }

        var plant = plants[i];

        this.plantsToClear.Add(plant);

        cleared.Add(
          new PlantState(
            this.EntityManager.GetComponentData<PrefabRef>(plant).m_Prefab,
            transforms[i],
            this.EntityManager.TryGetComponent<PseudoRandomSeed>(plant, out var seed)
              ? seed.m_Seed
              : (ushort) 0,
            this.EntityManager.TryGetComponent<Tree>(plant, out var tree) ? tree : null,
            this.EntityManager.HasComponent<Native>(plant)
          )
        );
      }

      return [.. cleared];
    }

    /// <summary>
    /// Whether the ground under a breakwater changed since its stones were laid, which Fit to
    /// terrain answers.
    /// </summary>
    internal bool IsOutdated(Entity record) {
      if (!this.IsBreakwater(record)) {
        return false;
      }

      var host = new GameLayoutHost(this.terrainSystem.GetHeightData(waitForPending: true));

      var polygon = this.EntityManager.GetBuffer<BreakwaterVertex>(record, true)
        .AsNativeArray()
        .Select(static vertex => vertex.position)
        .ToArray();

      return host.GroundHashOf(polygon) !=
        this.EntityManager.GetComponentData<BreakwaterRecord>(record).groundHash;
    }

    /// <summary>
    /// How each chosen prefab fares with these parameters: how many stones it lists, and how many
    /// of them the size tolerance and the protection let through.
    /// </summary>
    internal StoneVerdict[] Judge(IReadOnlyList<Entity> choice, BreakwaterParameters parameters) {
      var stones = this.catalog.Expand(choice);

      var protectable = stones
        .Where(prefab =>
          this.protection.Unavailable is not null ||
          this.protection.Classify(this.EntityManager, prefab) != PrefabCare.Unprotectable
        )
        .ToList();

      var laid = protectable.Count == 0
        ? []
        : BreakwaterLayout
          .Kept(protectable.ConvertAll(this.catalog.Shape), parameters)
          .Kept.Select(index => protectable[index])
          .ToHashSet();

      return [
        .. choice.Select(prefab => {
            var own = this.catalog.Expand([prefab]);

            return new StoneVerdict(
              prefab,
              this.catalog.Name(prefab),
              own.Count,
              own.Count(laid.Contains),
              own.Count > 0 && own.All(stone => !protectable.Contains(stone))
            );
          }
        )
      ];
    }

    /// <summary>
    /// The prefab entities of a list of prefab ids, each as <see cref="IdOf"/> gives it or a static
    /// object's bare name, those the game lacks left out.
    /// </summary>
    internal Entity[] Resolve(IEnumerable<string> ids) => this.catalog.Resolve(ids);

    /// <summary>
    /// A prefab's id, which <see cref="Resolve"/> finds it by.
    /// </summary>
    internal string IdOf(Entity prefab) => this.catalog.Id(prefab);

    /// <summary>
    /// What a breakwater's record holds, or null when the entity is not a breakwater.
    /// </summary>
    internal BreakwaterState? Read(Entity record) {
      if (!this.IsBreakwater(record)) {
        return null;
      }

      var data = this.EntityManager.GetComponentData<BreakwaterRecord>(record);

      return new BreakwaterState(
        [
          .. this.EntityManager.GetBuffer<BreakwaterVertex>(record, true)
            .AsNativeArray()
            .Select(static vertex => vertex.position)
        ],
        [
          .. this.EntityManager.GetBuffer<BreakwaterStonePrefab>(record, true)
            .AsNativeArray()
            .Select(static stone => stone.prefab)
        ],
        data.Parameters,
        data.protection,
        data.isToeByHand
          ? [
            .. this.EntityManager.GetBuffer<BreakwaterVertex>(record, true)
              .AsNativeArray()
              .Select(static vertex => vertex.isToe)
          ]
          : null,
        data.groundHash
      );
    }

    /// <summary>
    /// Whether an entity is a breakwater's record, neither deleted nor waiting for its deletion.
    /// </summary>
    internal bool IsBreakwater(Entity record) =>
      this.EntityManager.Exists(record) &&
      this.EntityManager.HasComponent<BreakwaterRecord>(record) &&
      !this.EntityManager.HasComponent<Deleted>(record) &&
      !(this.pending.TryGetValue(record, out var lay) && lay.Stones is null);

    /// <summary>
    /// Whether a prefab can be chosen: a stone, or a random prefab that lists one.
    /// </summary>
    internal bool CanLay(Entity prefab) => this.catalog.Expand([prefab]).Count > 0;

    /// <summary>
    /// The city's breakwaters.
    /// </summary>
    internal Entity[] List() => [
      .. this.recordsQuery.ToEntityArray(Allocator.Temp).Where(this.IsBreakwater)
    ];

    /// <summary>
    /// The stones linked to a breakwater, a duplicate that kept its link included.
    /// </summary>
    internal List<Entity> Stones(Entity record) {
      using var stones = this.stonesQuery.ToEntityArray(Allocator.Temp);
      using var links = this.stonesQuery.ToComponentDataArray<StoneLink>(Allocator.Temp);

      return [
        .. Enumerable.Range(0, stones.Length)
          .Where(i => links[i].record == record)
          .Select(i => stones[i])
      ];
    }

    protected override void OnCreate() {
      base.OnCreate();

      this.prefabSystem = this.World.GetOrCreateSystemManaged<PrefabSystem>();
      this.terrainSystem = this.World.GetOrCreateSystemManaged<TerrainSystem>();
      this.catalog = new StoneCatalog(this.prefabSystem, this.EntityManager);
      this.protection = new AnarchyProtection();

      this.recordsQuery = this.GetEntityQuery(
        ComponentType.ReadOnly<BreakwaterRecord>(),
        ComponentType.Exclude<Deleted>()
      );

      this.stonesQuery = this.GetEntityQuery(
        ComponentType.ReadOnly<StoneLink>(),
        ComponentType.Exclude<Deleted>(),
        ComponentType.Exclude<Temp>()
      );

      // A plant with an owner belongs to a building or a road, which lays it again.
      this.plantsQuery = this.GetEntityQuery(
        ComponentType.ReadOnly<Plant>(),
        ComponentType.ReadOnly<Transform>(),
        ComponentType.Exclude<Owner>(),
        ComponentType.Exclude<Deleted>(),
        ComponentType.Exclude<Temp>()
      );
    }

    protected override void OnGamePreload(Purpose purpose, GameMode mode) {
      base.OnGamePreload(purpose, mode);

      // Waiting work belongs to the city being left.
      this.pending.Clear();
      this.plantsToClear.Clear();
      this.plantsToLay.Clear();
      this.Revision++;
    }

    protected override void OnUpdate() {
      this.UpdatePlants();

      if (this.pending.Count == 0) {
        return;
      }

      var work = this.pending.ToArray();

      this.pending.Clear();

      foreach (var (record, lay) in work) {
        var current = this.Stones(record);

        this.Revision++;

        if (lay.Stones is null) {
          this.MarkDeleted(current);
          this.EntityManager.AddComponent<Deleted>(record);

          continue;
        }

        // The new stones first, never the old ones moved in place, which leaves the bridge's update
        // call unused: when they cannot be protected, the old ones and their record stay, and a
        // breakwater not laid yet goes.
        if (this.Lay(record, lay.Stones) is {} failure) {
          this.LastFailure = failure;

          if (lay.Before is {} before) {
            this.Write(record, before);
          }
          else {
            this.EntityManager.AddComponent<Deleted>(record);
          }

          Mod.Log.Error(
            $"{nameof(BreakwaterSystem)}: Breakwater {record} " +
            $"{(lay.Before is null ? "is not laid" : "kept its stones")}: {failure}."
          );

          continue;
        }

        this.LastFailure = null;
        this.MarkDeleted(current);
      }
    }

    /// <summary>
    /// Checks a breakwater's state and lays its stones out, without touching the city.
    /// </summary>
    /// <returns>Why nothing can be laid, or null.</returns>
    private string? Plan(ref BreakwaterState state, out StoneState[] stones) {
      stones = [];
      this.LastLayout = null;
      this.LastRefusal = StoneRefusal.None;

      if (!GameManager.instance.gameMode.IsGameOrEditor()) {
        return "no city or map is loaded";
      }

      if (state.Parameters.Validate() is {} invalid) {
        return invalid;
      }

      if (this.protection.Unavailable is {} unavailable) {
        return unavailable;
      }

      var chosen = this.catalog.Expand(state.Choice);

      var cares = chosen.ToDictionary(
        static prefab => prefab,
        prefab => this.protection.Classify(this.EntityManager, prefab)
      );

      var laid = chosen.Where(prefab => cares[prefab] != PrefabCare.Unprotectable).ToList();

      this.LastUnprotectable = [.. chosen.Except(laid)];

      if (this.LastUnprotectable.Count > 0) {
        Mod.Log.Warn(
          $"{nameof(BreakwaterSystem)}: Left out, as Anarchy cannot protect them: " +
          $"{string.Join(", ", this.LastUnprotectable.Select(this.catalog.Name))}."
        );
      }

      if (laid.Count == 0) {
        this.LastRefusal = chosen.Count == 0
          ? StoneRefusal.NoStoneChosen
          : StoneRefusal.Unprotectable;

        return chosen.Count == 0
          ? "no stone is chosen"
          : "none of the chosen stones can be protected by Anarchy";
      }

      var host = new GameLayoutHost(this.terrainSystem.GetHeightData(waitForPending: true));

      // Toe edges that no longer match the outline, or that hold none, hand the toe back to the
      // layout.
      if (state.Toe is {} toe && (toe.Length != state.Polygon.Length || !toe.Contains(true))) {
        state = state with {
          Toe = null
        };
      }

      state = state with {
        GroundHash = host.GroundHashOf(state.Polygon)
      };

      var shapes = laid.Select(this.catalog.Shape).ToList();
      var layout = BreakwaterLayout.Lay(state.Polygon, shapes, state.Parameters, host, state.Toe);

      this.LastLayout = layout;

      if (layout.WhyNothing is {} nothing) {
        return nothing;
      }

      var seed = state.Parameters.Seed;

      stones = [
        .. layout.Stones.Select((stone, index) => new StoneState(
            laid[stone.Shape],
            new Transform(stone.Position, stone.Rotation),
            BreakwaterSystem.Seed(seed, index)
          )
        )
      ];

      return null;
    }

    /// <summary>
    /// Creates a breakwater's stones and protects them, all or none.
    /// </summary>
    /// <returns>Why none are laid, or null.</returns>
    private string? Lay(Entity record, StoneState[] stones) {
      if (this.protection.Unavailable is {} unavailable) {
        return unavailable;
      }

      using var created = new NativeList<Entity>(stones.Length, Allocator.Temp);
      var anarchy = $"Anarchy {this.protection.Link?.Version}";
      string? failure = null;

      foreach (var group in stones.GroupBy(static stone => stone.Prefab)) {
        if (!this.catalog.IsStone(group.Key)) {
          failure = $"{this.catalog.Name(group.Key)} is not a stone, or is gone";

          break;
        }

        using var entities = this.CreateStones(group.Key, [.. group]);

        created.AddRange(entities);
        this.EntityManager.AddComponent<StoneLink>(entities);

        foreach (var entity in entities) {
          this.EntityManager.SetComponentData(entity, new StoneLink(record));
        }

        failure = this.protection.Classify(this.EntityManager, group.Key) switch {
          PrefabCare.Inert => null,
          PrefabCare.Unprotectable => $"{anarchy} cannot protect {this.catalog.Name(group.Key)}",
          _ => this.protection.Protect(this.EntityManager, entities) is var skipped and > 0
            ? $"{anarchy} skipped {skipped} of the {entities.Length} {this.catalog.Name(group.Key)}"
            : null
        };

        if (failure is not null) {
          break;
        }
      }

      // Seen by no system yet, the stones of a failed lay can go at once.
      if (failure is not null) {
        this.EntityManager.DestroyEntity(created.AsArray());
      }

      return failure;
    }

    /// <summary>
    /// Creates stones of one prefab.
    /// </summary>
    private NativeArray<Entity> CreateStones(Entity prefab, StoneState[] stones) {
      var entities = new NativeArray<Entity>(stones.Length, Allocator.Temp);
      var archetype = this.EntityManager.GetComponentData<ObjectData>(prefab).m_Archetype;

      // [PERF] In bulk, in place of one stone at a time.
      this.EntityManager.CreateEntity(archetype, entities);

      var hasSeed = this.EntityManager.HasComponent<PseudoRandomSeed>(entities[0]);

      for (var i = 0; i < stones.Length; i++) {
        this.EntityManager.SetComponentData(entities[i], new PrefabRef(prefab));
        this.EntityManager.SetComponentData(entities[i], stones[i].Transform);

        if (hasSeed) {
          this.EntityManager.SetComponentData(entities[i], new PseudoRandomSeed(stones[i].Seed));
        }
      }

      return entities;
    }

    /// <summary>
    /// Clears the plants waiting for it, and lays again those a revert brings back.
    /// </summary>
    private void UpdatePlants() {
      if (this.plantsToClear.Count > 0) {
        this.MarkDeleted([.. this.plantsToClear.Where(this.EntityManager.Exists)]);
        this.plantsToClear.Clear();
      }

      foreach (var plant in this.plantsToLay) {
        // A plant whose prefab went with its asset stays cleared.
        if (!this.EntityManager.TryGetComponent<ObjectData>(plant.Prefab, out var data)) {
          continue;
        }

        var entity = this.EntityManager.CreateEntity(data.m_Archetype);

        this.EntityManager.SetComponentData(entity, new PrefabRef(plant.Prefab));
        this.EntityManager.SetComponentData(entity, plant.Transform);

        if (this.EntityManager.HasComponent<PseudoRandomSeed>(entity)) {
          this.EntityManager.SetComponentData(entity, new PseudoRandomSeed(plant.Seed));
        }

        if (plant.Tree is {} tree && this.EntityManager.HasComponent<Tree>(entity)) {
          this.EntityManager.SetComponentData(entity, tree);
        }

        if (plant.IsNative) {
          this.EntityManager.AddComponent<Native>(entity);
        }
      }

      this.plantsToLay.Clear();
    }

    private void MarkDeleted(List<Entity> entities) {
      using var array = new NativeArray<Entity>([.. entities], Allocator.Temp);

      this.EntityManager.AddComponent<Deleted>(array);
    }

    /// <summary>
    /// Leaves a breakwater's stones for the next update, which puts its record back as it was
    /// before the first operation still waiting when they cannot be laid.
    /// </summary>
    /// <param name="stones">The stones to lay in place of the current ones, or null to delete the
    /// breakwater.</param>
    /// <param name="before">The record before this operation, or null for a breakwater not laid
    /// yet.</param>
    private void Queue(Entity record, StoneState[]? stones, BreakwaterState? before) =>
      this.pending[record] = new PendingLay(
        stones,
        this.pending.TryGetValue(record, out var waiting) ? waiting.Before : before
      );

    private void Write(Entity record, BreakwaterState state) {
      this.Revision++;

      this.EntityManager.SetComponentData(record, new BreakwaterRecord(state));

      var vertices = this.EntityManager.GetBuffer<BreakwaterVertex>(record);

      vertices.Clear();

      for (var i = 0; i < state.Polygon.Length; i++) {
        vertices.Add(new BreakwaterVertex(state.Polygon[i], state.Toe?[i] ?? false));
      }

      var prefabs = this.EntityManager.GetBuffer<BreakwaterStonePrefab>(record);

      prefabs.Clear();

      foreach (var prefab in state.Choice) {
        prefabs.Add(new BreakwaterStonePrefab(prefab));
      }
    }

    /// <summary>
    /// A stone's colour variation, the same for the same stone of the same layout.
    /// </summary>
    private static ushort Seed(uint layoutSeed, int index) =>
      (ushort) math.hash(new uint2(layoutSeed, (uint) index));

    private readonly record struct PendingLay(StoneState[]? Stones, BreakwaterState? Before);
  }

  /// <summary>
  /// What an edit changes of a breakwater, null keeping what it has.
  /// </summary>
  /// <param name="Toe">
  /// Per edge of the outline, whether it is toe; an empty list hands the toe back to the layout.
  /// </param>
  internal sealed record BreakwaterChange(
    IReadOnlyList<float2>? Polygon = null,
    IReadOnlyList<Entity>? Choice = null,
    BreakwaterParameters? Parameters = null,
    IReadOnlyList<bool>? Toe = null
  );

  /// <summary>
  /// How a chosen prefab fares: the stones it lists, itself when it is one, and how many of them
  /// are laid.
  /// </summary>
  /// <param name="IsUnprotectable">Whether Anarchy can protect none of its stones.</param>
  internal readonly record struct StoneVerdict(
    Entity Prefab,
    string Name,
    int Stones,
    int Laid,
    bool IsUnprotectable
  );

  /// <summary>
  /// Why the stones chosen let nothing be laid.
  /// </summary>
  internal enum StoneRefusal {
    None,

    /// <summary>
    /// The choice holds no stone.
    /// </summary>
    NoStoneChosen,

    /// <summary>
    /// Anarchy can protect none of the chosen stones.
    /// </summary>
    Unprotectable
  }
}
