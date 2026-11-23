using System;
using System.Collections.Generic;
using System.Linq;
using BreakwaterTool.Layout;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Hash128 = Colossal.Hash128;

namespace BreakwaterTool.Breakwaters;

/// <summary>
/// Which prefabs are stones, the stones a choice lays, and their shapes from their meshes.
/// A stone is a static object with a mesh, no plant, no building and no random prefab; a random
/// prefab in a choice lays the stones it lists, never itself.
/// </summary>
internal sealed class StoneCatalog(PrefabSystem prefabSystem, EntityManager entityManager) {
  // [PERF] Read once per prefab, in place of at each lay: a mesh does not change for the session.
  private readonly Dictionary<Entity, StoneShape> shapes = [];

  /// <summary>
  /// The prefab entities of a list of prefab ids, each as <see cref="Id"/> writes it or a static
  /// object's bare name, those the game lacks left out.
  /// </summary>
  internal Entity[] Resolve(IEnumerable<string> ids) => [
    .. ids
      .Select(static id => StoneCatalog.Parse(id.Trim()))
      .Select(id => prefabSystem.TryGetPrefab(id, out var prefab) &&
        prefabSystem.TryGetEntity(prefab, out var entity)
          ? entity
          : Entity.Null
      )
      .Where(static entity => entity != Entity.Null)
      .Distinct()
  ];

  /// <summary>
  /// A prefab's id, <c>Type/Name</c> or <c>Type/Name/Hash</c> escaped as in a URL, which
  /// <see cref="Resolve"/> finds it by; empty for an entity that is no prefab.
  /// </summary>
  // The game finds a prefab by its whole id, an asset's hash included (decompile: PrefabID.Equals,
  // PrefabSystem.TryGetPrefab).
  internal string Id(Entity prefab) =>
    prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var found)
      ? found.GetPrefabID().ToUrlSegment()
      : "";

  /// <summary>
  /// The stones a choice lays, each once, in the choice's order.
  /// </summary>
  internal List<Entity> Expand(IEnumerable<Entity> choice) =>
    choice
      .SelectMany(prefab => entityManager.HasBuffer<PlaceholderObjectElement>(prefab)
        ? entityManager.GetBuffer<PlaceholderObjectElement>(prefab, true)
          .ToNativeArray(Allocator.Temp)
          .Select(static variant => variant.m_Object)
          .ToArray()
        : [prefab]
      )
      .Where(this.IsStone)
      .Distinct()
      .ToList();

  internal bool IsStone(Entity prefab) =>
    prefab != Entity.Null &&
    entityManager.Exists(prefab) &&
    !entityManager.HasBuffer<PlaceholderObjectElement>(prefab) &&
    !entityManager.HasComponent<PlantData>(prefab) &&
    !entityManager.HasComponent<BuildingData>(prefab) &&
    entityManager.HasComponent<ObjectGeometryData>(prefab) &&
    // TryGetPrefab answers true with null for a prefab of another type.
    prefabSystem.TryGetPrefab<StaticObjectPrefab>(prefab, out var stone) &&
    stone is { m_Meshes.Length: > 0 };

  /// <summary>
  /// A stone's shape, from all its meshes at their place in the prefab.
  /// </summary>
  internal StoneShape Shape(Entity prefab) {
    if (this.shapes.TryGetValue(prefab, out var known)) {
      return known;
    }

    var vertices = new List<float3>();

    foreach (var info in prefabSystem.GetPrefab<StaticObjectPrefab>(prefab).m_Meshes) {
      if (info.m_Mesh is not RenderPrefab render) {
        continue;
      }

      // The meshes are loaded for the read and released after, as the renderer streams its own.
      // A mesh prefab without its geometry asset gives null (RenderPrefab.ObtainMeshes, read in the
      // decompile), which the game's own reads skip.
      if (render.ObtainMeshes() is not {} meshes) {
        continue;
      }

      foreach (var mesh in meshes) {
        vertices.AddRange(
          mesh.vertices.Select(vertex => info.m_Position + math.rotate(info.m_Rotation, vertex))
        );
      }

      render.ReleaseMeshes();
    }

    return this.shapes[prefab] = new StoneShape(vertices);
  }

  /// <summary>
  /// A prefab's name for a message.
  /// </summary>
  internal string Name(Entity prefab) =>
    prefabSystem.TryGetPrefab<PrefabBase>(prefab, out var named) ? named.name : prefab.ToString();

  /// <summary>
  /// The prefab id an id names, as <see cref="Id"/> writes it or a static object's bare name, as a
  /// style lists its stones.
  /// </summary>
  private static PrefabID Parse(string id) {
    var parts = id.Split('/').Select(Uri.UnescapeDataString).ToArray();

    return parts.Length switch {
      1 => new PrefabID(nameof(StaticObjectPrefab), parts[0]),
      2 => new PrefabID(parts[0], parts[1]),
      _ => new PrefabID(parts[0], parts[1], Hash128.Parse(parts[2]))
    };
  }
}
