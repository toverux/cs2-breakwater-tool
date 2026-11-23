using System.Collections.Generic;
using System.Linq;

namespace BreakwaterTool;

/// <summary>
/// The built-in styles: each a full set of layout parameters with the stones it is tuned for.
/// A style fills a breakwater's parameters and stones once; nothing keeps its name.
/// </summary>
internal static class BreakwaterStyles {
  /// <summary>
  /// The style a first breakwater takes, before any settings are kept.
  /// </summary>
  internal const string Default = "Coursed";

  /// <summary>
  /// The style named <see cref="Default"/>: boulders in even rows, a brick bond, the prototype's
  /// tuning on real sites.
  /// </summary>
  // Written before All, which lists it: static fields are set in the order they are written.
  internal static readonly StyleDefinition DefaultStyle = new(
    BreakwaterStyles.Default,
    BreakwaterParameters.Default,
    ["BoulderRandom01"]
  );

  internal static readonly IReadOnlyList<StyleDefinition> All = [
    BreakwaterStyles.DefaultStyle,

    // Boulders dumped at every angle, of every size, their rows broken up.
    new(
      "Rubble",
      BreakwaterParameters.Default with {
        Tumble = 0.35f,
        Irregularity = 0.25f,
        TurnJitter = 180f,
        SizeTolerance = 100f,
        Mixing = 1f,
        LevelTops = 0f
      },
      ["BoulderRandom01"]
    ),

    // Small rocks packed close on a slope, deep enough to hide the ground between them.
    new(
      "Riprap",
      BreakwaterParameters.Default with {
        Density = 2.4f,
        Embed = 0.35f,
        Irregularity = 0.15f,
        TurnJitter = 180f,
        SizeTolerance = 100f,
        Mixing = 1f
      },
      ["RockRandom01"]
    )
  ];

  internal static IReadOnlyList<string> Names { get; } = [
    .. BreakwaterStyles.All.Select(static style => style.Name)
  ];

  internal static StyleDefinition? Find(string name) =>
    BreakwaterStyles.All.FirstOrDefault(style => style.Name == name);
}

/// <param name="Name">The style's name, which its locale keys carry.</param>
/// <param name="Stones">The static object prefabs it lays, by name.</param>
internal sealed record StyleDefinition(
  string Name,
  BreakwaterParameters Parameters,
  IReadOnlyList<string> Stones
);
