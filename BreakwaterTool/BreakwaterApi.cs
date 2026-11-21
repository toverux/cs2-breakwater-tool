using System.Collections.Generic;
using BreakwaterTool.Breakwaters;
using Unity.Entities;
using Unity.Mathematics;

namespace BreakwaterTool;

/// <summary>
/// Breakwater Tool for other mods: lay, read, change and delete breakwaters as the tool does, so a
/// breakwater laid here is one like any other, saved with the city and edited by the tool.
/// Call it on the main thread, with a city or a map loaded.
/// A call that lays stones decides at once and returns why it lays nothing, if it does; the stones
/// change when the game next updates its objects, in the same frame when called before that.
/// Should Anarchy refuse the stones then, the failure is logged, an edit is undone and a new
/// breakwater is removed: <see cref="Read"/> then gives null.
/// </summary>
public static class BreakwaterApi {
  /// <summary>
  /// Lays a breakwater over a polygon on the terrain.
  /// </summary>
  /// <param name="polygon">The outline, in game metres seen from above (x, z), not closed.</param>
  /// <param name="stonePrefabs">
  /// The prefabs to lay: static objects with a mesh, or random prefabs whose variants are laid.
  /// </param>
  /// <param name="parameters">The layout, or null for the defaults.</param>
  public static BreakwaterOutcome Create(
    IReadOnlyList<float2> polygon,
    IReadOnlyList<Entity> stonePrefabs,
    BreakwaterParameters? parameters = null
  ) {
    var why = BreakwaterApi.System.Create(
      polygon,
      stonePrefabs,
      parameters ?? BreakwaterParameters.Default,
      out var record
    );

    return new BreakwaterOutcome(record, why);
  }

  /// <summary>
  /// Lays a breakwater again with what is given, keeping what is null. A call that lays nothing
  /// changes nothing.
  /// </summary>
  /// <returns>Why nothing is laid, or null.</returns>
  public static string? Edit(
    Entity breakwater,
    IReadOnlyList<float2>? polygon = null,
    IReadOnlyList<Entity>? stonePrefabs = null,
    BreakwaterParameters? parameters = null
  ) =>
    BreakwaterApi.System.Edit(breakwater, polygon, stonePrefabs, parameters);

  /// <summary>
  /// Lays a breakwater again as it is, on the terrain as it now is.
  /// Its stones stay where they were laid when the terrain changes: this re-seats them.
  /// </summary>
  /// <returns>Why nothing is laid, or null.</returns>
  public static string? FitToTerrain(Entity breakwater) =>
    BreakwaterApi.System.Edit(breakwater, null, null, null);

  /// <summary>
  /// Deletes a breakwater and its stones.
  /// </summary>
  /// <returns>Why nothing is deleted, or null.</returns>
  public static string? Delete(Entity breakwater) => BreakwaterApi.System.Delete(breakwater);

  /// <summary>
  /// A breakwater as it stands, or null when the entity is not one.
  /// </summary>
  public static BreakwaterInfo? Read(Entity breakwater) {
    if (BreakwaterApi.System.Read(breakwater) is not {} state) {
      return null;
    }

    return new BreakwaterInfo(
      state.Polygon,
      state.Choice,
      state.Parameters,
      [.. BreakwaterApi.System.Stones(breakwater)]
    );
  }

  /// <summary>
  /// The breakwaters of the city or map.
  /// </summary>
  public static Entity[] List() => BreakwaterApi.System.List();

  private static BreakwaterSystem System =>
    World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BreakwaterSystem>();
}

/// <param name="Breakwater">
/// The new breakwater, or <see cref="Entity.Null"/> when none. Should Anarchy refuse its stones
/// when they are laid, it is removed: <see cref="BreakwaterApi.Read"/> then gives null.
/// </param>
/// <param name="WhyNothing">Why nothing is laid, or null when the breakwater is.</param>
public sealed record BreakwaterOutcome(Entity Breakwater, string? WhyNothing);

/// <param name="Polygon">The outline, in game metres seen from above (x, z).</param>
/// <param name="StonePrefabs">The prefabs it lays, as chosen.</param>
/// <param name="Stones">Its stones, those another tool copied with their link included.</param>
public sealed record BreakwaterInfo(
  float2[] Polygon,
  Entity[] StonePrefabs,
  BreakwaterParameters Parameters,
  Entity[] Stones
);
