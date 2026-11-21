using System.Collections.Generic;
using System.Linq;
using BreakwaterTool.Layout;
using Unity.Mathematics;

namespace BreakwaterTool.Scenarios.Ground;

/// <summary>
/// The stones the sites lay off-engine, each as the layout sees the game's stone it stands for:
/// a box of two tiers, its base the whole stone's footprint, which sets its long axis, and its top
/// the footprint of the part above the embed line, which the layout lays.
/// The numbers are the game's, measured in each stone's long axis frame at the default embed; the
/// Proving ground scenario checks them against the game's stones.
/// </summary>
internal static class FixtureStones {
  /// <summary>
  /// The eight boulders of <c>BoulderRandom01</c>, Boulder01 to Boulder08. The size tolerance
  /// leaves Boulder02 out, a quarter smaller than the median.
  /// </summary>
  internal static IReadOnlyList<StoneShape> Boulders { get; } = FixtureStones.Boxes(
    [
      // The whole footprint's size and centre, the shown footprint's size and centre, bottom, top.
      [5.310f, 4.316f, 0.139f, -0.005f, 4.643f, 4.056f, 0.173f, -0.135f, -1.347f, 2.432f],
      [4.216f, 3.988f, 0.221f, -0.172f, 3.874f, 3.974f, 0.139f, -0.179f, -1.643f, 2.434f],
      [4.769f, 4.729f, -0.042f, 0.149f, 4.592f, 4.690f, -0.130f, 0.149f, -1.237f, 1.837f],
      [5.447f, 4.358f, -0.078f, -0.003f, 4.995f, 4.129f, 0.069f, 0.112f, -1.301f, 2.584f],
      [4.785f, 4.696f, 0.001f, -0.017f, 4.660f, 4.250f, -0.061f, 0.206f, -1.327f, 2.090f],
      [4.536f, 4.486f, 0.113f, -0.143f, 4.293f, 4.218f, -0.009f, -0.010f, -1.652f, 2.529f],
      [5.303f, 4.049f, -0.073f, -0.286f, 5.034f, 3.994f, 0.062f, -0.271f, -1.355f, 1.822f],
      [5.170f, 4.564f, -0.039f, -0.035f, 4.966f, 4.259f, 0.062f, -0.188f, -1.829f, 1.760f]
    ]
  );

  /// <summary>
  /// The vanilla <c>CratePile01</c>, for the sites that need many stones, as no vanilla stone is
  /// that small.
  /// </summary>
  internal static IReadOnlyList<StoneShape> Crates { get; } =
    FixtureStones.Boxes([[2.045f, 1.218f, 0f, 0f, 2.045f, 1.218f, 0f, 0f, 0f, 2.385f]]);

  internal static IReadOnlyList<StoneShape> For(SiteStones stones) =>
    stones is SiteStones.Boulders ? FixtureStones.Boulders : FixtureStones.Crates;

  private static StoneShape[] Boxes(float[][] table) => [
    .. table.Select(static row => FixtureStones.Box(row))
  ];

  private static StoneShape Box(float[] row) {
    var bottom = row[8];
    var top = row[9];

    return new StoneShape(
      [
        .. FixtureStones.Corners(new float2(row[0], row[1]), new float2(row[2], row[3]), bottom),
        .. FixtureStones.Corners(new float2(row[4], row[5]), new float2(row[6], row[7]), top)
      ]
    );
  }

  private static IEnumerable<float3> Corners(float2 size, float2 centre, float y) {
    var half = size / 2f;

    yield return new float3(centre.x - half.x, y, centre.y - half.y);
    yield return new float3(centre.x + half.x, y, centre.y - half.y);
    yield return new float3(centre.x + half.x, y, centre.y + half.y);
    yield return new float3(centre.x - half.x, y, centre.y + half.y);
  }
}
