using System.Collections.Generic;
using System.Linq;
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
  /// <param name="toeEdges">
  /// Per edge of the polygon, from its point i to i + 1, whether it is toe, where the rows start;
  /// null leaves the choice to the layout, which takes the low edges.
  /// </param>
  public static BreakwaterOutcome Create(
    IReadOnlyList<float2> polygon,
    IReadOnlyList<Entity> stonePrefabs,
    BreakwaterParameters? parameters = null,
    IReadOnlyList<bool>? toeEdges = null
  ) {
    var why = BreakwaterApi.System.Create(
      new BreakwaterState(
        [.. polygon],
        [.. stonePrefabs],
        parameters ?? BreakwaterParameters.Default,
        StoneProtectionKind.Anarchy,
        toeEdges?.ToArray()
      ),
      out var record
    );

    return new BreakwaterOutcome(record, why);
  }

  /// <summary>
  /// Lays a breakwater again with what is given, keeping what is null. A call that lays nothing
  /// changes nothing.
  /// </summary>
  /// <param name="toeEdges">
  /// Per edge of the polygon, whether it is toe; an empty list hands the choice back to the
  /// layout, as does a list that no longer matches the polygon.
  /// </param>
  /// <returns>Why nothing is laid, or null.</returns>
  public static string? Edit(
    Entity breakwater,
    IReadOnlyList<float2>? polygon = null,
    IReadOnlyList<Entity>? stonePrefabs = null,
    BreakwaterParameters? parameters = null,
    IReadOnlyList<bool>? toeEdges = null
  ) =>
    BreakwaterApi.System.Edit(
      breakwater,
      new BreakwaterChange(polygon, stonePrefabs, parameters, toeEdges)
    );

  /// <summary>
  /// Lays a breakwater again as it is, on the terrain as it now is.
  /// Its stones stay where they were laid when the terrain changes: this re-seats them.
  /// </summary>
  /// <returns>Why nothing is laid, or null.</returns>
  public static string? FitToTerrain(Entity breakwater) =>
    BreakwaterApi.System.Edit(breakwater, new BreakwaterChange());

  /// <summary>
  /// Whether the terrain under a breakwater changed since its stones were laid, which
  /// <see cref="FitToTerrain"/> answers.
  /// </summary>
  public static bool IsOutdated(Entity breakwater) => BreakwaterApi.System.IsOutdated(breakwater);

  /// <summary>
  /// Removes the plants inside a breakwater's outline, trees and bushes, those of a building or a
  /// road excepted, when the game next updates its objects.
  /// </summary>
  /// <returns>How many plants it removes.</returns>
  public static int ClearVegetation(Entity breakwater) =>
    BreakwaterApi.System.ClearVegetation(breakwater).Length;

  /// <summary>
  /// The names of the built-in styles, for <see cref="Style"/>.
  /// </summary>
  public static IReadOnlyList<string> Styles => BreakwaterStyles.Names;

  /// <summary>
  /// A built-in style: a layout and the stones it is tuned for, to pass to <see cref="Create"/> or
  /// <see cref="Edit"/>. A breakwater keeps the layout and the stones, never the style.
  /// </summary>
  /// <returns>
  /// The style, its stones those the game holds, or null when no style has that name.
  /// </returns>
  public static BreakwaterStyle? Style(string name) =>
    BreakwaterStyles.Find(name) is {} style
      ? new BreakwaterStyle(
        style.Name,
        style.Parameters,
        BreakwaterApi.System.Resolve(style.Stones)
      )
      : null;

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
      [.. BreakwaterApi.System.Stones(breakwater)],
      state.Toe
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
/// <param name="ToeEdges">
/// Per edge of the polygon, whether it is toe, where they were given; null where the layout chooses
/// them.
/// </param>
public sealed record BreakwaterInfo(
  float2[] Polygon,
  Entity[] StonePrefabs,
  BreakwaterParameters Parameters,
  Entity[] Stones,
  bool[]? ToeEdges
);

/// <param name="Name">The style's name, as <see cref="BreakwaterApi.Styles"/> lists it.</param>
/// <param name="StonePrefabs">The prefabs the style lays, those the game holds.</param>
public sealed record BreakwaterStyle(
  string Name,
  BreakwaterParameters Parameters,
  Entity[] StonePrefabs
);
