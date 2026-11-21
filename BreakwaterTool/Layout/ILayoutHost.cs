using Unity.Mathematics;

namespace BreakwaterTool.Layout;

/// <summary>
/// What the layout needs from where it runs: the terrain's heights, and a way to fill its distance
/// field. The game's adapter samples the terrain and runs the job on Burst; a test's samples a
/// fixture grid and runs the job's body in a loop.
/// </summary>
internal interface ILayoutHost {
  /// <summary>
  /// The terrain's height at a point, in game metres.
  /// </summary>
  float HeightAt(float2 xz);

  /// <summary>
  /// Fills every cell of the field the job writes, by calling its <c>Execute</c> once per cell.
  /// </summary>
  void Fill(ToeDistanceJob job);
}
