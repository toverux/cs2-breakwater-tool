using System;
using BreakwaterTool.Layout;
using Unity.Mathematics;
using Xunit;

namespace BreakwaterTool.Tests.Layout;

public sealed class GroundCheckTests {
  private static readonly float2[]
    Square = [new(0f, 0f), new(10f, 0f), new(10f, 10f), new(0f, 10f)];

  // An L: the square without its top right quarter, a notch the outline's bounds still span.
  private static readonly float2[] Ell = [
    new(0f, 0f), new(10f, 0f), new(10f, 5f), new(5f, 5f), new(5f, 10f), new(0f, 10f)
  ];

  [Fact]
  public void Judge_LevelGround_IsLevel() {
    Assert.Equal(
      GroundVerdict.Level,
      GroundCheck.Judge(GroundCheckTests.Square, static _ => 3f, GroundCheckTests.Dry)
    );
  }

  [Fact]
  public void Judge_ReliefJustUnderTheLimit_IsLevel() {
    Assert.Equal(
      GroundVerdict.Level,
      GroundCheck.Judge(
        GroundCheckTests.Square,
        GroundCheckTests.RiseAtTheEastSide(GroundCheck.LevelRelief - 0.1f),
        GroundCheckTests.Dry
      )
    );
  }

  [Fact]
  public void Judge_ReliefOfTheLimit_IsShaped() {
    Assert.Equal(
      GroundVerdict.Shaped,
      GroundCheck.Judge(
        GroundCheckTests.Square,
        GroundCheckTests.RiseAtTheEastSide(GroundCheck.LevelRelief),
        GroundCheckTests.Dry
      )
    );
  }

  [Fact]
  public void Judge_AMoundInsideTheOutline_IsShaped() {
    // A mound 3 m wide around the middle, away from every vertex.
    Assert.Equal(
      GroundVerdict.Shaped,
      GroundCheck.Judge(
        GroundCheckTests.Square,
        static at => math.distance(at, new float2(5f, 5f)) < 1.5f ? 5f : 0f,
        GroundCheckTests.Dry
      )
    );
  }

  [Fact]
  public void Judge_AMoundInTheNotchOfAConcaveOutline_IsLevel() {
    Assert.Equal(
      GroundVerdict.Level,
      GroundCheck.Judge(
        GroundCheckTests.Ell,
        static at => math.distance(at, new float2(8f, 8f)) < 1.5f ? 5f : 0f,
        GroundCheckTests.Dry
      )
    );
  }

  [Fact]
  public void Judge_WhollyUnderWater_IsUnderwater() {
    Assert.Equal(
      GroundVerdict.Underwater,
      GroundCheck.Judge(
        GroundCheckTests.Square,
        GroundCheckTests.RiseAtTheEastSide(10f),
        static _ => 5f
      )
    );
  }

  [Fact]
  public void Judge_UnderWaterButAPoint_IsJudgedByItsRelief() {
    Assert.Equal(
      GroundVerdict.Level,
      GroundCheck.Judge(
        GroundCheckTests.Square,
        static _ => 0f,
        static at => at.Equals(new float2(10f, 10f)) ? 0f : 5f
      )
    );
  }

  [Fact]
  public void Judge_UnderTheFilmOfWaterAShoreCarries_IsNotUnderwater() {
    Assert.Equal(
      GroundVerdict.Level,
      GroundCheck.Judge(GroundCheckTests.Square, static _ => 0f, static _ => GroundCheck.WetDepth)
    );
  }

  [Fact]
  public void Judge_UnderThreePoints_IsShaped() {
    Assert.Equal(
      GroundVerdict.Shaped,
      GroundCheck.Judge([new float2(0f, 0f), new float2(10f, 0f)], static _ => 0f, static _ => 5f)
    );
  }

  [Theory]
  [InlineData(2f, 2f, true)]
  [InlineData(8f, 2f, true)]
  [InlineData(2f, 8f, true)]
  [InlineData(8f, 8f, false)]
  [InlineData(12f, 2f, false)]
  [InlineData(-1f, 5f, false)]
  public void Contains_APointAndAConcaveOutline_TellsWhetherItIsInside(
    float x,
    float z,
    bool isInside
  ) {
    Assert.Equal(isInside, GroundCheck.Contains(GroundCheckTests.Ell, new float2(x, z)));
  }

  private static float Dry(float2 at) => 0f;

  /// <summary>
  /// The ground level at 0 but along the square's east side, at x = 10, raised by a relief.
  /// </summary>
  private static Func<float2, float> RiseAtTheEastSide(float relief) =>
    at => at.x >= 10f ? relief : 0f;
}
