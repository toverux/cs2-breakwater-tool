using System.Collections.Generic;
using System.Linq;
using BreakwaterTool.Layout;
using BreakwaterTool.Scenarios.Ground;
using Unity.Mathematics;
using Xunit;

namespace BreakwaterTool.Tests.Layout;

/// <summary>
/// What each layout parameter the tool's panel sets does to the stones, one rule a parameter, and
/// a picture of each one that shows from above.
/// </summary>
public sealed class LayoutParameterTests {
  /// <summary>
  /// A revetment 40 m along its toe at z = 0, rising 0.5 m per metre to its crest at z = 10.
  /// </summary>
  private static readonly float2[] Revetment = [
    new(0f, 0f), new(40f, 0f), new(40f, 10f), new(0f, 10f)
  ];

  /// <summary>
  /// The revetment with a point on its right side, 3 m up: its corners stand at 0, 0, 3, 5 and 5 m.
  /// </summary>
  private static readonly float2[] Stepped = [
    new(0f, 0f), new(40f, 0f), new(40f, 6f), new(40f, 10f), new(0f, 10f)
  ];

  /// <summary>
  /// A revetment wide and deep enough for a hundred boulders, a point halfway up its right side.
  /// </summary>
  private static readonly float2[] Wide = [
    new(0f, 0f), new(50f, 0f), new(50f, 10f), new(50f, 20f), new(0f, 20f)
  ];

  private static readonly FixtureHost Slope = new(static xz => 0.5f * xz.y);

  /// <summary>
  /// The slope's unit normal, which a stone that follows the slope stands along.
  /// </summary>
  private static readonly float3 SlopeNormal = math.normalize(new float3(0f, 1f, -0.5f));

  /// <summary>
  /// The defaults, each stone on its place, along its row and upright, seated by its bottom.
  /// </summary>
  private static readonly BreakwaterParameters Straight = BreakwaterParameters.Default with {
    Irregularity = 0f,
    TurnJitter = 0f,
    FollowSlope = 0f,
    LevelTops = 0f
  };

  /// <summary>
  /// A box 2 m long, 1.4 m wide and 1 m tall, centred on its pivot seen from above, its pivot at
  /// its base.
  /// </summary>
  private static readonly StoneShape Box = LayoutParameterTests.BoxOf(height: 1f);

  /// <summary>
  /// The same box, 3 m tall.
  /// </summary>
  private static readonly StoneShape Tall = LayoutParameterTests.BoxOf(height: 3f);

  /// <summary>
  /// A picture's name and what moves from the defaults in it, on the wide revetment.
  /// </summary>
  private static readonly Dictionary<string, BreakwaterParameters> Looks = new() {
    ["look-default"] = BreakwaterParameters.Default,
    ["look-density-low"] = BreakwaterParameters.Default with {
      Density = 1f
    },
    ["look-density-high"] = BreakwaterParameters.Default with {
      Density = 3f
    },
    ["look-row-ratio"] = BreakwaterParameters.Default with {
      RowRatio = 1.5f
    },
    ["look-irregularity"] = BreakwaterParameters.Default with {
      Irregularity = 0.4f
    },
    ["look-reshuffled"] = BreakwaterParameters.Default with {
      Seed = 2
    },
    ["look-long-axis-across"] = BreakwaterParameters.Default with {
      IsLongAxisAcross = true
    },
    ["look-turn-jitter"] = BreakwaterParameters.Default with {
      TurnJitter = 180f
    },
    ["look-size-tolerance"] = BreakwaterParameters.Default with {
      SizeTolerance = 100f
    },
    ["look-mixing"] = BreakwaterParameters.Default with {
      Mixing = 1f
    },
    ["look-two-layers"] = BreakwaterParameters.Default with {
      Layers = 2
    },
    ["look-tumble"] = BreakwaterParameters.Default with {
      Tumble = 0.35f
    },
    ["look-toe-share"] = BreakwaterParameters.Default with {
      ToeShare = 0.6f
    },
    ["look-level-height"] = BreakwaterParameters.Default with {
      LevelHeight = 20f
    }
  };

  public static TheoryData<string> Pictures { get; } = [.. LayoutParameterTests.Looks.Keys];

  [Theory]
  [InlineData(1f)]
  [InlineData(2f)]
  [InlineData(4f)]
  public void Lay_Density_SharesAFootprintsAreaAmongThatManyStones(float density) {
    var result = LayoutParameterTests.Lay(
      BreakwaterParameters.Default with {
        Density = density
      }
    );

    // The box shows 2 m by 1.4 m: at 1 the footprints meet edge to edge.
    Assert.Equal(2f * 1.4f / density, result.StoneSpacing * result.RowSpacing, 1e-3f);
  }

  [Fact]
  public void Lay_TwiceTheDensity_LaysAboutTwiceTheStones() {
    var once = LayoutParameterTests.Lay(
      BreakwaterParameters.Default with {
        Density = 1f
      }
    );

    var twice = LayoutParameterTests.Lay(
      BreakwaterParameters.Default with {
        Density = 2f
      }
    );

    // But for the rows' ends and the last row under the crest.
    Assert.InRange((float) twice.Stones.Count / once.Stones.Count, 1.7f, 2.3f);
  }

  [Theory]
  [InlineData(0.5f)]
  [InlineData(1f)]
  [InlineData(2f)]
  public void Lay_RowRatio_SpacesTheRowsByThatManyStoneSpacingsInFootprints(float ratio) {
    var result = LayoutParameterTests.Lay(
      BreakwaterParameters.Default with {
        RowRatio = ratio
      }
    );

    // The box is 1.4 m across its row and 2 m along it.
    Assert.Equal(ratio * 1.4f / 2f, result.RowSpacing / result.StoneSpacing, 1e-4f);
  }

  [Theory]
  [InlineData(0f)]
  [InlineData(0.25f)]
  [InlineData(0.75f)]
  public void Lay_Embed_SinksEachStoneThatShareOfItsHeight(float embed) {
    var result = LayoutParameterTests.Lay(
      LayoutParameterTests.Straight with {
        Embed = embed
      }
    );

    Assert.NotEmpty(result.Stones);

    // The box is 1 m tall and its pivot at its base.
    Assert.All(
      result.Stones,
      stone => Assert.Equal((0.5f * stone.Position.z) - embed, stone.Position.y, 1e-3f)
    );
  }

  [Fact]
  public void Lay_Irregularity_MovesEachStoneOffItsPlaceByNoMore() {
    var even = LayoutParameterTests.Lay(LayoutParameterTests.Straight);

    var irregular = LayoutParameterTests.Lay(
      LayoutParameterTests.Straight with {
        Irregularity = 0.3f
      }
    );

    // The two draw the same numbers, so their stones pair up.
    var offsets = even.Stones
      .Zip(
        irregular.Stones,
        static (place, stone) => math.abs(stone.Position.xz - place.Position.xz)
      )
      .ToArray();

    var most = 0.3f * even.StoneSpacing;

    Assert.Equal(even.Stones.Count, irregular.Stones.Count);
    Assert.All(offsets, offset => Assert.InRange(math.cmax(offset), 0f, most + 1e-3f));
    Assert.Contains(offsets, offset => math.cmax(offset) > 0.8f * most);
  }

  [Fact]
  public void Lay_LevelTopsOff_SeatsEachStoneByItsBottom() {
    var result = LayoutParameterTests.LayMixed(levelTops: 0f);

    Assert.Equal([0, 1], result.Stones.Select(static stone => stone.Shape).Distinct().Order());

    // Half of its own height under the ground, the pivot at the base: 0.5 m and 1.5 m.
    Assert.All(
      result.Stones,
      static stone => Assert.Equal(
        (0.5f * stone.Position.z) - (stone.Shape == 0 ? 0.5f : 1.5f),
        stone.Position.y,
        1e-3f
      )
    );
  }

  [Fact]
  public void Lay_LevelTopsOn_StandsEveryTopAtTheSameHeightOverTheGround() {
    var result = LayoutParameterTests.LayMixed(levelTops: 1f);

    Assert.Equal([0, 1], result.Stones.Select(static stone => stone.Shape).Distinct().Order());

    // Half of the mean height over the ground, 1 m, whichever box it is.
    Assert.All(
      result.Stones,
      static stone => Assert.Equal(
        (0.5f * stone.Position.z) + 1f,
        stone.Position.y + (stone.Shape == 0 ? 1f : 3f),
        1e-3f
      )
    );
  }

  [Fact]
  public void Lay_LongAxisAcross_TurnsEachStoneAcrossItsRow() {
    var result = LayoutParameterTests.Lay(
      LayoutParameterTests.Straight with {
        IsLongAxisAcross = true
      }
    );

    // The stones are spaced by their short side along the row, at the default packing of 0.75.
    Assert.Equal(1.4f * 0.75f, result.StoneSpacing, 1e-4f);
    Assert.All(
      result.Stones,
      static stone => Assert.InRange(LayoutParameterTests.Cosine(stone), 0f, 1e-3f)
    );
  }

  [Theory]
  [InlineData(0f)]
  [InlineData(1f)]
  public void Lay_FollowSlope_StandsEachStoneUprightOrOnTheTerrain(float followSlope) {
    var result = LayoutParameterTests.Lay(
      LayoutParameterTests.Straight with {
        FollowSlope = followSlope
      }
    );

    var expected = followSlope == 0f ? math.up() : LayoutParameterTests.SlopeNormal;

    Assert.All(
      result.Stones,
      stone => Assert.InRange(
        math.distance(math.rotate(stone.Rotation, math.up()), expected),
        0f,
        1e-3f
      )
    );
  }

  [Fact]
  public void Lay_TurnJitter_TurnsEachStoneOffItsRowByNoMore() {
    var result = LayoutParameterTests.Lay(
      LayoutParameterTests.Straight with {
        TurnJitter = 30f
      }
    );

    var cosines = result.Stones.Select(LayoutParameterTests.Cosine).ToArray();

    Assert.All(
      cosines,
      static cosine => Assert.InRange(cosine, math.cos(math.radians(30f)) - 1e-3f, 1f)
    );

    // Some stone uses most of it.
    Assert.Contains(cosines, static cosine => cosine < math.cos(math.radians(24f)));
  }

  [Fact]
  public void Lay_NoTurnJitter_LaysEachStoneAlongItsRow() {
    var result = LayoutParameterTests.Lay(LayoutParameterTests.Straight);

    Assert.All(
      result.Stones,
      static stone => Assert.InRange(LayoutParameterTests.Cosine(stone), 1f - 1e-4f, 1f)
    );
  }

  [Theory]
  // Boulder02 alone is a quarter smaller than the median.
  [InlineData(0.15f, new[] { 1 })]
  [InlineData(100f, new int[0])]
  // The median itself is always laid.
  [InlineData(0f, new[] { 0, 1, 2, 3, 4, 5, 7 })]
  public void Lay_SizeTolerance_LeavesOutTheStonesTooFarFromTheMedian(float tolerance, int[] left) {
    var result = BreakwaterLayout.Lay(
      LayoutParameterTests.Wide,
      FixtureStones.Boulders,
      BreakwaterParameters.Default with {
        SizeTolerance = tolerance,
        Mixing = 1f
      },
      LayoutParameterTests.Slope
    );

    Assert.Equal(LayoutVerdict.Laid, result.Verdict);
    Assert.Equal(left, result.LeftOut);
    Assert.Equal(
      Enumerable.Range(0, FixtureStones.Boulders.Count).Except(left),
      result.Stones.Select(static stone => stone.Shape).Distinct().Order()
    );
  }

  [Fact]
  public void Lay_NoMixing_BuildsEachRowOfOneStone() {
    var rows = LayoutParameterTests.Rows(LayoutParameterTests.LayMixed(levelTops: 0f, mixing: 0f));

    Assert.All(
      rows,
      static row => Assert.Single(row.Select(static stone => stone.Shape).Distinct())
    );

    // Each row draws its own.
    Assert.Equal(2, rows.Select(static row => row.First().Shape).Distinct().Count());
  }

  [Fact]
  public void Lay_FullMixing_DrawsEveryStoneOfARow() {
    var rows = LayoutParameterTests.Rows(LayoutParameterTests.LayMixed(levelTops: 0f, mixing: 1f));

    // A row of twenty-six stones all alike has one chance in thirty million.
    Assert.All(
      rows.Where(static row => row.Count() > 20),
      static row => Assert.Equal(2, row.Select(static stone => stone.Shape).Distinct().Count())
    );
  }

  [Theory]
  [InlineData(0f)]
  [InlineData(0.4f)]
  [InlineData(1f)]
  public void Lay_UnderLayerDepth_DropsTheSecondLayerThatShareOfAStone(float depth) {
    var first = LayoutParameterTests.Lay(LayoutParameterTests.Straight);

    var both = LayoutParameterTests.Lay(
      LayoutParameterTests.Straight with {
        Layers = 2,
        UnderLayerDepth = depth
      }
    );

    Assert.Equal(first.Stones, both.Stones.Take(first.Stones.Count));
    Assert.True(both.Stones.Count > 1.5f * first.Stones.Count);

    // The box is 1 m tall, half of it under the ground.
    Assert.All(
      both.Stones.Skip(first.Stones.Count),
      stone => Assert.Equal((0.5f * stone.Position.z) - 0.5f - depth, stone.Position.y, 1e-3f)
    );
  }

  [Theory]
  [InlineData(0.25f)]
  [InlineData(0.5f)]
  [InlineData(1f)]
  public void Lay_Tumble_TurnsEachStoneAboutItsPlaceOnTheGroundByNoMore(float tumble) {
    var laid = LayoutParameterTests.Lay(LayoutParameterTests.Straight);

    var dumped = LayoutParameterTests.Lay(
      LayoutParameterTests.Straight with {
        Tumble = tumble
      }
    );

    // With nothing else drawn that moves a stone, the two lay the same places in the same order.
    Assert.Equal(laid.Stones.Count, dumped.Stones.Count);

    var leans = new List<float>();

    foreach (var (place, stone) in laid.Stones.Zip(dumped.Stones)) {
      var ground = new float3(place.Position.x, 0.5f * place.Position.z, place.Position.z);

      var up = math.rotate(stone.Rotation, math.up());

      // The stone turns about its place on the ground, which stays half a stone up its own axis.
      Assert.InRange(math.distance(stone.Position, ground - (0.5f * up)), 0f, 2e-3f);

      leans.Add(math.degrees(math.acos(math.clamp(up.y, -1f, 1f))));
    }

    // A turn of an angle leans a stone by no more: at 1, up to upside down.
    Assert.All(leans, lean => Assert.InRange(lean, 0f, (tumble * 180f) + 0.1f));
    Assert.Contains(leans, lean => lean > 0.5f * tumble * 180f);
  }

  [Theory]
  [InlineData(0.4f, new[] { true, false, false, false, false })]
  [InlineData(0.7f, new[] { true, true, false, false, false })]
  [InlineData(1f, new[] { true, true, true, true, true })]
  public void ToeEdges_ToeShare_TakesTheEdgesUnderThatShareOfTheHeights(float share, bool[] toe) {
    var edges = BreakwaterLayout.ToeEdges(
      LayoutParameterTests.Stepped,
      LayoutParameterTests.Slope,
      BreakwaterParameters.Default with {
        ToeShare = share
      }
    );

    Assert.Equal(toe, edges);
  }

  [Theory]
  // The outline's corners span 5 m.
  [InlineData(4.9f, false)]
  [InlineData(5.1f, true)]
  public void ToeEdges_LevelHeight_TakesEveryEdgeOfAnOutlineThatLevel(float height, bool isLevel) {
    var edges = BreakwaterLayout.ToeEdges(
      LayoutParameterTests.Stepped,
      LayoutParameterTests.Slope,
      BreakwaterParameters.Default with {
        LevelHeight = height
      }
    );

    Assert.Equal(
      isLevel ? [true, true, true, true, true] : [true, false, false, false, false],
      edges
    );
  }

  [Fact]
  public void Lay_ToeEdgesGiven_StartsTheRowsFromThem() {
    // The crest and the left side, where the heights would take the bottom edge.
    bool[] given = [false, false, true, true];

    var result = BreakwaterLayout.Lay(
      LayoutParameterTests.Revetment,
      [LayoutParameterTests.Box],
      LayoutParameterTests.Straight,
      LayoutParameterTests.Slope,
      given
    );

    Assert.Equal(LayoutVerdict.Laid, result.Verdict);
    Assert.Equal(given, result.ToeEdges);

    // A row lies on the crest, at z = 10, where the heights' toe leaves the last row short of it.
    Assert.InRange(
      result.Stones.Count(static stone => math.abs(stone.Position.z - 10f) < 0.05f),
      20,
      30
    );
  }

  [Theory]
  [InlineData(new[] { false, false, false, false })]
  [InlineData(new[] { true, true, true })]
  [InlineData(new[] { true, true, true, true, true })]
  public void Lay_ToeEdgesWithNoToeOrOfAnotherCount_LeavesTheChoiceToTheHeights(bool[] given) {
    var result = BreakwaterLayout.Lay(
      LayoutParameterTests.Revetment,
      [LayoutParameterTests.Box],
      LayoutParameterTests.Straight,
      LayoutParameterTests.Slope,
      given
    );

    Assert.Equal([true, false, false, false], result.ToeEdges);
  }

  [Fact]
  public void Styles_EachOne_IsNamedOnceAndLaysTheRevetment() {
    Assert.Contains(BreakwaterStyles.Default, BreakwaterStyles.Names);
    Assert.Equal(BreakwaterStyles.Names.Distinct(), BreakwaterStyles.Names);

    Assert.All(
      BreakwaterStyles.All,
      static style => {
        Assert.Null(style.Parameters.Validate());
        Assert.NotEmpty(style.Stones);

        var result = BreakwaterLayout.Lay(
          LayoutParameterTests.Revetment,
          FixtureStones.Boulders,
          style.Parameters,
          LayoutParameterTests.Slope
        );

        Assert.Equal(LayoutVerdict.Laid, result.Verdict);
      }
    );
  }

  [Theory]
  [MemberData(nameof(LayoutParameterTests.Pictures))]
  public void Lay_Look_MatchesItsApprovedPicture(string name) {
    var parameters = LayoutParameterTests.Looks[name];

    var outline = LayoutParameterTests.Wide;

    var result = BreakwaterLayout.Lay(
      outline,
      FixtureStones.Boulders,
      parameters,
      LayoutParameterTests.Slope
    );

    Assert.Equal(LayoutVerdict.Laid, result.Verdict);

    // The second layer's stones follow the first's, which a single layer lays alike.
    var first = BreakwaterLayout.Lay(
        outline,
        FixtureStones.Boulders,
        parameters with {
          Layers = 1
        },
        LayoutParameterTests.Slope
      )
      .Stones.Count;

    PictureApproval.Verify(
      name,
      LayoutPicture.Render(
        outline,
        result.ToeEdges,
        (
          "laid",
          LayoutPicture.Footprints(
            result.Stones.Take(first),
            FixtureStones.Boulders,
            parameters.Embed
          )
        ),
        (
          "other",
          LayoutPicture.Footprints(
            result.Stones.Skip(first),
            FixtureStones.Boulders,
            parameters.Embed
          )
        )
      )
    );
  }

  /// <summary>
  /// The cosine of the angle between a box's long side and its row, which runs along x, seen from
  /// above.
  /// </summary>
  private static float Cosine(LaidStone box) =>
    math.abs(math.normalize(math.rotate(box.Rotation, new float3(1f, 0f, 0f)).xz).x);

  /// <summary>
  /// The rows up the revetment, a row spacing apart on the slope, sqrt(1.25) times less seen from
  /// above.
  /// </summary>
  private static IGrouping<int, LaidStone>[] Rows(LayoutResult result) => [
    .. result.Stones
      .GroupBy(stone =>
        (int) math.round(stone.Position.z * math.sqrt(1.25f) / result.RowSpacing)
      )
      .OrderBy(static row => row.Key)
  ];

  private static LayoutResult Lay(BreakwaterParameters parameters) =>
    BreakwaterLayout.Lay(
      LayoutParameterTests.Revetment,
      [LayoutParameterTests.Box],
      parameters,
      LayoutParameterTests.Slope
    );

  /// <summary>
  /// The revetment laid with the two boxes, which differ by their height alone.
  /// </summary>
  private static LayoutResult LayMixed(float levelTops, float mixing = 1f) =>
    BreakwaterLayout.Lay(
      LayoutParameterTests.Revetment,
      [LayoutParameterTests.Box, LayoutParameterTests.Tall],
      LayoutParameterTests.Straight with {
        LevelTops = levelTops,
        Mixing = mixing
      },
      LayoutParameterTests.Slope
    );

  private static StoneShape BoxOf(float height) =>
    new(
      [
        .. from x in new[] { -1f, 1f }
        from y in new[] { 0f, height }
        from z in new[] { -0.7f, 0.7f }
        select new float3(x, y, z)
      ]
    );
}
