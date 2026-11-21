using System.Collections.Generic;
using Unity.Mathematics;

namespace BreakwaterTool.Layout;

/// <summary>
/// A stone the layout lays: which of the shapes it was given, and where.
/// </summary>
/// <param name="Position">Where the prefab's pivot goes.</param>
/// <param name="Shape">An index into the shapes the layout was given.</param>
internal readonly record struct LaidStone(int Shape, float3 Position, quaternion Rotation);

internal enum LayoutVerdict {
  /// <summary>
  /// The stones are laid.
  /// </summary>
  Laid,

  /// <summary>
  /// The polygon has fewer than three points, a point that is not a finite number, or no area.
  /// </summary>
  NoArea,

  /// <summary>
  /// No stone shape was given, or none shows a footprint once embedded.
  /// </summary>
  NoStone,

  /// <summary>
  /// No row of stones fits in the polygon.
  /// </summary>
  NoRow,

  /// <summary>
  /// The estimate of the stones, or the stones laid, exceed <see cref="BreakwaterLayout.Guard"/>:
  /// nothing is laid.
  /// </summary>
  OverGuard
}

/// <summary>
/// What the layout laid, or why it laid nothing, and what it found on the way.
/// </summary>
internal sealed class LayoutResult {
  internal required LayoutVerdict Verdict { get; init; }

  /// <summary>
  /// The stones, empty unless <see cref="Verdict"/> is <see cref="LayoutVerdict.Laid"/>.
  /// </summary>
  internal IReadOnlyList<LaidStone> Stones { get; init; } = [];

  /// <summary>
  /// The number of stones the polygon's area holds at the computed spacings, which the guard
  /// judges.
  /// </summary>
  internal int Estimate { get; init; }

  /// <summary>
  /// Per edge of the polygon, from its point i to i + 1, whether it belongs to the toe.
  /// </summary>
  internal IReadOnlyList<bool> ToeEdges { get; init; } = [];

  /// <summary>
  /// The indices of the shapes the size tolerance left out.
  /// </summary>
  internal IReadOnlyList<int> LeftOut { get; init; } = [];

  internal int Rows { get; init; }

  /// <summary>
  /// The cells of the distance field the rows climb, at most <see cref="ToeField.Cap"/>.
  /// </summary>
  internal int FieldCells { get; init; }

  internal float StoneSpacing { get; init; }

  internal float RowSpacing { get; init; }

  /// <summary>
  /// Why nothing was laid, in plain words for a log or a caller, or null when stones were.
  /// </summary>
  internal string? WhyNothing =>
    this.Verdict switch {
      LayoutVerdict.Laid => null,
      LayoutVerdict.NoArea => "the polygon has no area",
      LayoutVerdict.NoStone => "no stone shows a footprint to lay",
      LayoutVerdict.NoRow => "no row of stones fits in the polygon",
      LayoutVerdict.OverGuard => this.Estimate > BreakwaterLayout.Guard
        ? $"about {this.Estimate} stones, over the limit of {BreakwaterLayout.Guard}"
        // Laying stopped at the guard, short of what the polygon holds.
        : $"more stones than the limit of {BreakwaterLayout.Guard}",
      _ => this.Verdict.ToString()
    };
}
