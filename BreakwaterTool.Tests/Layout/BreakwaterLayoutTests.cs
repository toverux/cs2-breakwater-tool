using System.Collections.Generic;
using System.Linq;
using BreakwaterTool.Layout;
using BreakwaterTool.Scenarios.Ground;
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
  /// The defaults, each stone on its place and along its row.
  /// </summary>
  private static readonly BreakwaterParameters Straight = BreakwaterParameters.Default with {
    Irregularity = 0f,
    TurnJitter = 0f
  };

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
  public void Lay_Revetment_KeepsTheInvariants() {
    var result = BreakwaterLayoutTests.LayRevetment(BreakwaterParameters.Default);

    Assert.Equal(LayoutVerdict.Laid, result.Verdict);
    Assert.Null(BreakwaterLayoutTests.Breach(result));
  }

  [Fact]
  public void Lay_Revetment_CoversThePolygonAtTheEstimatedDensity() {
    var result = BreakwaterLayoutTests.LayRevetment(BreakwaterParameters.Default);

    // On a slope of 0.5, rows a row spacing apart on the ground are 0.98 / sqrt(1.25) = 0.877 m
    // apart seen from above, so 400 m² hold about 400 / (1.5 * 0.877) = 304 stones.
    Assert.InRange(result.Stones.Count, 274, 334);
  }

  [Fact]
  public void Lay_Revetment_BondsEachRowWithTheOneBelow() {
    var result = BreakwaterLayoutTests.LayRevetment(
      BreakwaterParameters.Default with {
        Irregularity = 0f
      }
    );

    var rows = BreakwaterLayoutTests.Rows(result, new float2(0f, 1f));

    Assert.Equal(result.Rows, rows.Length);

    // Past the toe, each stone stands half a stone spacing along from a stone of the row below.
    Assert.All(
      rows.Zip(rows.Skip(1)),
      pair => Assert.All(
        pair.Second,
        stone => Assert.Contains(
          pair.First,
          below => math.abs(
              math.abs(stone.Position.x - below.Position.x) - (0.5f * result.StoneSpacing)
            ) <
            0.01f
        )
      )
    );
  }

  [Fact]
  public void Lay_RevetmentTurned_StartsEachRowAtItsEdge() {
    // Turned, so that the starts of the rows round each their own way.
    Assert.All(
      Enumerable.Range(0, 20).Select(static turn => math.radians(18f * turn)),
      static angle => {
        math.sincos(angle, out var sin, out var cos);

        var along = new float2(cos, sin);
        var up = new float2(-sin, cos);

        var result = BreakwaterLayout.Lay(
          [.. BreakwaterLayoutTests.Revetment.Select(point => (point.x * along) + (point.y * up))],
          [BreakwaterLayoutTests.Box],
          BreakwaterParameters.Default with {
            Irregularity = 0f
          },
          new FixtureHost(xz => 0.5f * math.dot(xz, up))
        );

        // On the edge, then half a stone spacing in, in turn.
        Assert.All(
          BreakwaterLayoutTests.Rows(result, up),
          row => Assert.InRange(
            row.Min(stone => math.dot(stone.Position.xz, along)),
            -0.01f,
            0.55f * result.StoneSpacing
          )
        );
      }
    );
  }

  // Level along the grid, then each width turned its own way, level and on a ridge.
  [Theory]
  [InlineData(0f, 0f)]
  [InlineData(9f, 0f)]
  [InlineData(9f, 0.6f)]
  public void Lay_Strip_LaysNoTwoRowsFarApart(float turn, float slope) {
    // Boulders, whose rows are farther apart than the ground's tilt is read over.
    var stones = FixtureStones.Boulders;

    var parameters = BreakwaterParameters.Default with {
      Irregularity = 0f
    };

    // Its rows climb from both its sides and meet along its middle: wider and wider by as much as
    // adds a row on each side.
    Assert.All(
      Enumerable.Range(0, 40),
      n => {
        math.sincos(math.radians(turn * n), out var sin, out var cos);

        var along = new float2(cos, sin);
        var across = new float2(-sin, cos);
        var half = 9f + (0.075f * n);

        var result = BreakwaterLayout.Lay(
          [
            (-60f * along) - (half * across),
            (60f * along) - (half * across),
            (60f * along) + (half * across),
            (-60f * along) + (half * across)
          ],
          stones,
          parameters,
          new FixtureHost(xz => slope * (half - math.abs(math.dot(xz, across))))
        );

        Assert.Equal(LayoutVerdict.Laid, result.Verdict);

        var centres = result.Stones
          .Select(stone => LayoutPicture.Centre(stone, stones[stone.Shape], parameters.Embed))
          .ToArray();

        // Seen from above, rows are as much closer as the ground is steep.
        var rowSpacing = result.RowSpacing * math.cos(math.atan(slope));

        // Clear of the ends, over two stones of each row at a time.
        for (var at = -30f; at < 30f; at += result.StoneSpacing) {
          var rows = centres
            .Where(centre => math.abs(math.dot(centre, along) - at) < result.StoneSpacing)
            .Select(centre => math.dot(centre, across))
            .Order()
            .ToArray();

          // The two sides close from up to 1.7 row spacings under the middle, in three gaps.
          Assert.All(
            rows.Zip(rows.Skip(1)),
            pair => Assert.InRange(pair.Second - pair.First, 0f, 1.14f * rowSpacing)
          );
        }
      }
    );
  }

  [Fact]
  public void Lay_SteepRidge_KeepsTheInvariants() {
    // Rows far apart, of stones close together: seen from above, a slope brings the rows closer,
    // not the stones along one.
    var stones = FixtureStones.Boulders;

    var parameters = BreakwaterParameters.Default with {
      RowRatio = 2.5f
    };

    Assert.All(
      Enumerable.Range(0, 40),
      n => {
        var half = 9f + (0.1f * n);
        float2[] outline = [new(-40f, -half), new(40f, -half), new(40f, half), new(-40f, half)];

        var result = BreakwaterLayout.Lay(
          outline,
          stones,
          parameters,
          new FixtureHost(xz => half - math.abs(xz.y))
        );

        Assert.Equal(LayoutVerdict.Laid, result.Verdict);
        Assert.Null(LayoutInvariants.Breach(outline, result, stones, parameters));
      }
    );
  }

  [Fact]
  public void Lay_InnerBend_TakesMoreStonesUphill() {
    // Ten rows of boulders up a bowl, from a toe 15 m in radius: each is longer than the one below.
    var stones = FixtureStones.Boulders;
    float2[] outline = [.. Arc(15f), .. Arc(45f).Reverse()];

    var result = BreakwaterLayout.Lay(
      outline,
      stones,
      BreakwaterParameters.Default,
      new FixtureHost(static xz => 0.3f * math.length(xz))
    );

    // As many as a row of the stones' spacing holds, on a slope of 0.3, and still in the bond.
    var each = result.StoneSpacing * result.RowSpacing / math.sqrt(1.09f);

    Assert.InRange(result.Stones.Count * each / BreakwaterLayout.Area(outline), 0.97f, 1.06f);

    Assert.InRange(
      BrickBond.BrokenShare(outline, result, stones, BreakwaterParameters.Default),
      0f,
      BrickBond.MostBroken
    );

    static IEnumerable<float2> Arc(float radius) =>
      Enumerable.Range(0, 25)
        .Select(i => radius * new float2(math.cos(math.PI * i / 24f), math.sin(math.PI * i / 24f)));
  }

  [Fact]
  public void Lay_StripNarrowerThanAStone_LaysEachStoneAlongItsRow() {
    math.sincos(math.radians(30f), out var sin, out var cos);

    var along = new float2(cos, sin);
    var up = new float2(-sin, cos);

    var result = BreakwaterLayout.Lay(
      [float2.zero, 0.5f * along, (0.5f * along) + (8f * up), 8f * up],
      [BreakwaterLayoutTests.Box],
      BreakwaterLayoutTests.Straight,
      new FixtureHost(xz => 0.5f * math.dot(xz, up))
    );

    Assert.All(
      result.Stones,
      stone => Assert.InRange(BreakwaterLayoutTests.Cosine(stone, along), 0.99f, 1.01f)
    );
  }

  [Fact]
  public void Lay_LevelPad_LaysItsSpineAlongIt() {
    math.sincos(math.radians(65f), out var sin, out var cos);

    var along = new float2(cos, sin);
    var across = new float2(-sin, cos);

    // Of any length, so that some have a top as small as a cell of the field.
    Assert.All(
      Enumerable.Range(0, 16).Select(static n => 10f + (0.25f * n)),
      length => {
        var result = BreakwaterLayout.Lay(
          [float2.zero, length * along, (length * along) + (6f * across), 6f * across],
          [BreakwaterLayoutTests.Box],
          BreakwaterLayoutTests.Straight,
          new FixtureHost(static _ => 0f)
        );

        // On the pad's middle line, clear of the ends, where the rows turn round the corners.
        var spine = result.Stones.Where(stone =>
          math.abs(math.dot(stone.Position.xz, across) - 3f) < 0.5f * result.RowSpacing &&
          math.abs(math.dot(stone.Position.xz, along) - (0.5f * length)) < (0.5f * length) - 3f
        );

        Assert.All(
          spine,
          stone => Assert.InRange(BreakwaterLayoutTests.Cosine(stone, along), 0.98f, 1.01f)
        );
      }
    );
  }

  [Fact]
  public void Lay_NarrowLevelStrip_LaysAsManyStonesAnyWayItRuns() {
    Assert.InRange(Count(45f), 0.9f * Count(0f), 1.1f * Count(0f));

    static int Count(float turn) {
      math.sincos(math.radians(turn), out var sin, out var cos);

      var along = 40f * new float2(cos, sin);
      var across = 1.8f * new float2(-sin, cos);

      return BreakwaterLayout.Lay(
          [float2.zero, along, along + across, across],
          FixtureStones.Crates,
          BreakwaterLayoutTests.Straight,
          new FixtureHost(static _ => 0f)
        )
        .Stones.Count;
    }
  }

  [Fact]
  public void Lay_SpitOffTheToe_ClosesNoRowAwayFromIt() {
    // A bank 40 m along its toe, and a spit 10 m long and 1.4 m wide that its toe goes on along.
    math.sincos(math.radians(82f), out var sin, out var cos);

    var along = new float2(cos, sin);
    var up = new float2(-sin, cos);

    float2[] bank = [
      new(0f, 0f), new(50f, 0f), new(50f, 1.4f), new(40f, 1.4f), new(40f, 10f), new(0f, 10f)
    ];

    var result = BreakwaterLayout.Lay(
      [.. bank.Select(point => (point.x * along) + (point.y * up))],
      FixtureStones.Crates,
      BreakwaterLayoutTests.Straight,
      new FixtureHost(xz => 0.5f * math.dot(xz, up))
    );

    var rise = result.RowSpacing / math.sqrt(1.25f);

    // Between the toe's row and the next, clear of the spit.
    Assert.DoesNotContain(
      result.Stones.Select(static stone => LayoutPicture.Centre(
          stone,
          FixtureStones.Crates[stone.Shape],
          BreakwaterLayoutTests.Straight.Embed
        )
      ),
      centre => math.dot(centre, along) < 35f &&
        math.dot(centre, up) > 0.1f * rise &&
        math.dot(centre, up) < 0.9f * rise
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
    Assert.Null(BreakwaterLayoutTests.Breach(result));
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

  /// <summary>
  /// The cosine of the angle between a box's long side and a direction, seen from above.
  /// </summary>
  private static float Cosine(LaidStone box, float2 direction) =>
    math.abs(
      math.dot(math.normalize(math.rotate(box.Rotation, new float3(1f, 0f, 0f)).xz), direction)
    );

  /// <summary>
  /// The rows up the revetment, a row spacing apart on the slope, sqrt(1.25) times less seen from
  /// above.
  /// </summary>
  private static IGrouping<int, LaidStone>[] Rows(LayoutResult result, float2 up) => [
    .. result.Stones
      .GroupBy(stone =>
        (int) math.round(math.dot(stone.Position.xz, up) * math.sqrt(1.25f) / result.RowSpacing)
      )
      .OrderBy(static row => row.Key)
  ];

  private static string? Breach(LayoutResult result) =>
    LayoutInvariants.Breach(
      BreakwaterLayoutTests.Revetment,
      result,
      [BreakwaterLayoutTests.Box],
      BreakwaterParameters.Default
    );

  private static LayoutResult LayRevetment(BreakwaterParameters parameters) =>
    BreakwaterLayout.Lay(
      BreakwaterLayoutTests.Revetment,
      [BreakwaterLayoutTests.Box],
      parameters,
      BreakwaterLayoutTests.Slope
    );
}
