using System.Linq;
using BreakwaterTool.Layout;
using Unity.Mathematics;
using Xunit;

namespace BreakwaterTool.Tests.Layout;

public sealed class BreakwaterLayoutTests {
  /// <summary>
  /// A revetment 40 m along its toe at z = 0, rising 0.5 m per metre to its crest at z = 10.
  /// </summary>
  private static readonly float2[] Revetment = [
    new(0f, 0f), new(40f, 0f), new(40f, 10f), new(0f, 10f)
  ];

  private static readonly FixtureHost Slope = new(static xz => 0.5f * xz.y);

  /// <summary>
  /// A box 2 m long, 1.4 m wide and 1 m tall, centred on its pivot seen from above.
  /// Its pivot is at its base.
  /// </summary>
  private static readonly StoneShape Box = new(
    [
      .. from x in new[] { -1f, 1f }
      from y in new[] { 0f, 1f }
      from z in new[] { -0.7f, 0.7f }
      select new float3(x, y, z)
    ]
  );

  [Fact]
  public void Lay_Revetment_SpacesStonesAndRowsByTheirFootprint() {
    var result = BreakwaterLayoutTests.LayRevetment(BreakwaterParameters.Default);

    Assert.Equal(LayoutVerdict.Laid, result.Verdict);

    // The default packing is 0.75 along the row and 0.7 across it.
    Assert.Equal(1.5f, result.StoneSpacing, 1e-4f);
    Assert.Equal(0.98f, result.RowSpacing, 1e-4f);
    Assert.Equal([true, false, false, false], result.ToeEdges);
    Assert.Equal(272, result.Estimate);
  }

  [Fact]
  public void Lay_Revetment_CoversThePolygonAtTheEstimatedDensity() {
    var result = BreakwaterLayoutTests.LayRevetment(BreakwaterParameters.Default);

    // On a slope of 0.5, rows a row spacing apart on the ground are 0.98 / sqrt(1.25) = 0.877 m
    // apart seen from above, so 400 m² hold about 400 / (1.5 * 0.877) = 304 stones.
    Assert.InRange(result.Stones.Count, 274, 334);

    Assert.All(
      result.Stones,
      static stone => {
        Assert.InRange(stone.Position.x, -1f, 41f);
        Assert.InRange(stone.Position.z, -1f, 11f);
      }
    );
  }

  [Fact]
  public void Lay_HalfEmbeddedLevelTops_SinksEachStoneHalfItsHeight() {
    var result = BreakwaterLayoutTests.LayRevetment(BreakwaterParameters.Default);

    // With every stone the mean height, by its bottom and by its top agree: the pivot, at the base,
    // sits half the box's height under the ground at its place.
    Assert.All(
      result.Stones,
      static stone => Assert.Equal((0.5f * stone.Position.z) - 0.5f, stone.Position.y, 1e-3f)
    );
  }

  [Fact]
  public void Lay_SameSeed_LaysTheSameStones() {
    var first = BreakwaterLayoutTests.LayRevetment(BreakwaterParameters.Default);
    var second = BreakwaterLayoutTests.LayRevetment(BreakwaterParameters.Default);
    var reseeded = BreakwaterLayoutTests.LayRevetment(
      BreakwaterParameters.Default with {
        Seed = 2
      }
    );

    Assert.Equal(first.Stones, second.Stones);
    Assert.NotEqual(first.Stones, reseeded.Stones);
  }

  [Fact]
  public void Lay_DiagonalSlope_TakesTheEdgesAtTheLowestCorner() {
    // Falling towards (40, 0): the lowest corner is a point, and no whole edge lies in the bottom
    // share of the heights.
    var diagonal = new FixtureHost(static xz => (0.1f * (40f - xz.x)) + (0.3f * xz.y));

    var result = BreakwaterLayout.Lay(
      BreakwaterLayoutTests.Revetment,
      [BreakwaterLayoutTests.Box],
      BreakwaterParameters.Default,
      diagonal
    );

    Assert.Equal([true, true, false, false], result.ToeEdges);
    Assert.Equal(LayoutVerdict.Laid, result.Verdict);
    Assert.NotEmpty(result.Stones);
  }

  [Fact]
  public void Lay_TwoPoints_HasNoArea() {
    var result = BreakwaterLayout.Lay(
      [new float2(0f, 0f), new float2(10f, 0f)],
      [BreakwaterLayoutTests.Box],
      BreakwaterParameters.Default,
      BreakwaterLayoutTests.Slope
    );

    Assert.Equal(LayoutVerdict.NoArea, result.Verdict);
    Assert.Empty(result.Stones);
  }

  [Fact]
  public void Lay_NoShape_HasNoStone() {
    var result = BreakwaterLayout.Lay(
      BreakwaterLayoutTests.Revetment,
      [],
      BreakwaterParameters.Default,
      BreakwaterLayoutTests.Slope
    );

    Assert.Equal(LayoutVerdict.NoStone, result.Verdict);
  }

  [Fact]
  public void Lay_OverTheGuard_LaysNothing() {
    // 300 m by 300 m hold about 90 000 / (1.5 * 0.98) = 61 224 stones.
    var result = BreakwaterLayout.Lay(
      [new float2(0f, 0f), new float2(300f, 0f), new float2(300f, 300f), new float2(0f, 300f)],
      [BreakwaterLayoutTests.Box],
      BreakwaterParameters.Default,
      BreakwaterLayoutTests.Slope
    );

    Assert.Equal(LayoutVerdict.OverGuard, result.Verdict);
    Assert.Equal(61_224, result.Estimate);
    Assert.Empty(result.Stones);
  }

  private static LayoutResult LayRevetment(BreakwaterParameters parameters) =>
    BreakwaterLayout.Lay(
      BreakwaterLayoutTests.Revetment,
      [BreakwaterLayoutTests.Box],
      parameters,
      BreakwaterLayoutTests.Slope
    );
}
