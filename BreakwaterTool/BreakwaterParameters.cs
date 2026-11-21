using System;
using System.Linq;

namespace BreakwaterTool;

/// <summary>
/// How a breakwater's stones are laid, everything of its look but its polygon and its stones.
/// The first four are the essentials a player sets; the rest refine them.
/// Lengths are not parameters: they come from the stones' meshes, which these scale.
/// </summary>
public sealed record BreakwaterParameters {
  /// <summary>
  /// The defaults, the prototype's tuning on real sites.
  /// </summary>
  public static readonly BreakwaterParameters Default = new();

  /// <summary>
  /// How dense the stones are: their visible footprints' area over the area they are laid on.
  /// At 1 the footprints meet edge to edge; above, they overlap, which closes the gaps between
  /// rounded stones. From 0.01 to 100.
  /// </summary>
  public float Density { get; init; } = 1f / (0.75f * 0.7f);

  /// <summary>
  /// How deep the stones sit: the fraction of a stone's height under the ground, from 0 to 1.
  /// </summary>
  public float Embed { get; init; } = 0.5f;

  /// <summary>
  /// How irregular the rows are: each stone's random offset from its place, in stone spacings, up
  /// to 1.
  /// </summary>
  public float Irregularity { get; init; } = 0.03f;

  /// <summary>
  /// The draw of every random choice: the same inputs and seed lay the same stones, and another
  /// seed reshuffles them.
  /// </summary>
  public uint Seed { get; init; } = 1;

  /// <summary>
  /// The distance between rows over the distance between stones in a row, each measured in the
  /// stones' footprints across and along the row. From 0.01 to 100.
  /// </summary>
  public float RowRatio { get; init; } = 0.7f / 0.75f;

  /// <summary>
  /// 0 seats each stone by its bottom, 1 by its top, so that every top stands at the same height
  /// over the ground.
  /// </summary>
  public float LevelTops { get; init; } = 0.5f;

  /// <summary>
  /// Lays each stone with its long axis across its row instead of along it.
  /// </summary>
  public bool IsLongAxisAcross { get; init; }

  /// <summary>
  /// 0 keeps the stones upright, 1 tilts them to the terrain.
  /// </summary>
  public float FollowSlope { get; init; } = 1f;

  /// <summary>
  /// The largest random turn of a stone about the vertical, in degrees, up to 180.
  /// </summary>
  public float TurnJitter { get; init; } = 6f;

  /// <summary>
  /// Lays only the stones whose footprint area is within this fraction of the median one: uniform
  /// sizes are what makes rows read. At 1 it keeps those up to twice the median area, and at most
  /// it is 100.
  /// </summary>
  public float SizeTolerance { get; init; } = 0.15f;

  /// <summary>
  /// 0 builds each row of one stone, 1 draws every stone at random.
  /// </summary>
  public float Mixing { get; init; } = 0.3f;

  /// <summary>
  /// 1 or 2: a second layer repeats the rows half a row and half a stone off, lower, to fill the
  /// gaps of the first.
  /// </summary>
  public int Layers { get; init; } = 1;

  /// <summary>
  /// How far the second layer drops, in the stones' mean height, up to 1.
  /// </summary>
  public float UnderLayerDepth { get; init; } = 0.4f;

  /// <summary>
  /// The random tumble of each stone about all three axes, 0 for stones laid in rows, up to 1 for
  /// stones dumped at every angle.
  /// </summary>
  public float Tumble { get; init; }

  /// <summary>
  /// The fraction of a footprint between two stones' centres along a row, from
  /// <see cref="Density"/> and <see cref="RowRatio"/>.
  /// </summary>
  internal float Packing => 1f / (float) Math.Sqrt(this.Density * this.RowRatio);

  /// <summary>
  /// The fraction of a footprint between two rows, from <see cref="Density"/> and
  /// <see cref="RowRatio"/>.
  /// </summary>
  internal float RowPacking => this.RowRatio * this.Packing;

  /// <summary>
  /// Why these parameters cannot lay a breakwater, or null when they can.
  /// </summary>
  public string? Validate() =>
    this.OutOfRange() ??
    (this.Layers is not (1 or 2)
      ? $"{nameof(this.Layers)} must be 1 or 2, not {this.Layers}."
      : null);

  /// <summary>
  /// Why a float parameter is out of its range, or null. The bounds keep the stone spacings within
  /// a hundred footprints, where the layout's arithmetic holds, and refuse NaN and the infinities
  /// by the same comparison.
  /// </summary>
  private string? OutOfRange() {
    (string Name, float Value, float Min, float Max)[] floats = [
      (nameof(this.Density), this.Density, 0.01f, 100f),
      (nameof(this.Embed), this.Embed, 0f, 1f),
      (nameof(this.Irregularity), this.Irregularity, 0f, 1f),
      (nameof(this.RowRatio), this.RowRatio, 0.01f, 100f),
      (nameof(this.LevelTops), this.LevelTops, 0f, 1f),
      (nameof(this.FollowSlope), this.FollowSlope, 0f, 1f),
      (nameof(this.TurnJitter), this.TurnJitter, 0f, 180f),
      (nameof(this.SizeTolerance), this.SizeTolerance, 0f, 100f),
      (nameof(this.Mixing), this.Mixing, 0f, 1f),
      (nameof(this.UnderLayerDepth), this.UnderLayerDepth, 0f, 1f),
      (nameof(this.Tumble), this.Tumble, 0f, 1f)
    ];

    return floats
      .Where(static parameter =>
        !(parameter.Value >= parameter.Min && parameter.Value <= parameter.Max)
      )
      .Select(static parameter =>
        $"{parameter.Name} must be from {parameter.Min} to {parameter.Max}, not {parameter.Value}."
      )
      .FirstOrDefault();
  }
}
