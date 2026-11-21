using System.Collections.Generic;
using System.Linq;
using BreakwaterTool.Layout;
using BreakwaterTool.Scenarios.Ground;
using BreakwaterTool.Tests.Scenarios.Ground;
using Unity.Mathematics;
using Xunit;

namespace BreakwaterTool.Tests.Layout;

/// <summary>
/// Outlines another mod may pass through the public entry points: each reaches a verdict, and none
/// throws or hangs.
/// </summary>
public sealed class LayoutInputTests {
  /// <summary>
  /// Random outlines per run, the same ones every run.
  /// </summary>
  private const int FuzzCount = 300;

  private static readonly FixtureHost Slope = new(static xz => 0.5f * xz.y);

  private static readonly float2[]
    Square = [new(0f, 0f), new(40f, 0f), new(40f, 10f), new(0f, 10f)];

  /// <summary>
  /// Each hostile outline, by what it is, and the verdict it must reach.
  /// </summary>
  private static readonly Dictionary<string, (float2[] Outline, LayoutVerdict Verdict)> Outlines =
    new() {
      ["a point not a number"] = (
        [new float2(0f, 0f), new float2(float.NaN, 0f), new float2(10f, 10f)],
        LayoutVerdict.NoArea),
      ["an infinite point"] = (
        [new float2(0f, 0f), new float2(float.PositiveInfinity, 0f), new float2(10f, 10f)],
        LayoutVerdict.NoArea),
      ["points in a line"] = ([new float2(0f, 0f), new float2(10f, 0f), new float2(20f, 0f)],
        LayoutVerdict.NoArea),
      ["a bow-tie of no area"] = (
        [new float2(0f, 0f), new float2(40f, 10f), new float2(40f, 0f), new float2(0f, 10f)],
        LayoutVerdict.NoArea),
      ["a repeated point"] = (
      [
        new float2(0f, 0f),
        new float2(0f, 0f),
        new float2(40f, 0f),
        new float2(40f, 10f),
        new float2(0f, 10f)
      ], LayoutVerdict.Laid),
      ["a clockwise outline"] = (
        [new float2(0f, 0f), new float2(0f, 10f), new float2(40f, 10f), new float2(40f, 0f)],
        LayoutVerdict.Laid),
      ["an outline off the map"] = (
      [
        new float2(20000f, 20000f),
        new float2(20040f, 20000f),
        new float2(20040f, 20010f),
        new float2(20000f, 20010f)
      ], LayoutVerdict.Laid),
      ["an outline of 10 000 km"] = (
        [new float2(0f, 0f), new float2(1e7f, 0f), new float2(1e7f, 1e7f), new float2(0f, 1e7f)],
        LayoutVerdict.OverGuard),
      // Its lobes cancel in the signed area, so the estimate before laying is far too low.
      ["a wide bow-tie of little area"] = (
        [new float2(0f, 0f), new float2(400f, 400f), new float2(400f, 0f), new float2(0f, 390f)],
        LayoutVerdict.OverGuard)
    };

  public static TheoryData<string> Hostile { get; } = [.. LayoutInputTests.Outlines.Keys];

  [Theory]
  [MemberData(nameof(LayoutInputTests.Hostile))]
  public void Lay_HostileOutline_ReachesItsVerdict(string what) {
    var (outline, verdict) = LayoutInputTests.Outlines[what];

    var result = BreakwaterLayout.Lay(
      outline,
      FixtureStones.Crates,
      BreakwaterParameters.Default,
      LayoutInputTests.Slope
    );

    Assert.Equal(verdict, result.Verdict);
  }

  [Fact]
  public void Lay_NoSizeTolerance_LaysTheMedianStone() {
    var result = BreakwaterLayout.Lay(
      LayoutInputTests.Square,
      FixtureStones.Boulders,
      BreakwaterParameters.Default with {
        SizeTolerance = 0f
      },
      LayoutInputTests.Slope
    );

    Assert.Equal(LayoutVerdict.Laid, result.Verdict);
  }

  [Fact]
  public void Lay_StoneOfNoFootprint_HasNoStone() {
    // Buried to its apex, a pyramid shows no footprint.
    StoneShape pyramid = new(
      [
        new float3(-1f, 0f, -1f),
        new float3(1f, 0f, -1f),
        new float3(1f, 0f, 1f),
        new float3(-1f, 0f, 1f),
        new float3(0f, 1f, 0f)
      ]
    );

    var result = BreakwaterLayout.Lay(
      LayoutInputTests.Square,
      [pyramid],
      BreakwaterParameters.Default with {
        Embed = 0.999f
      },
      LayoutInputTests.Slope
    );

    Assert.Equal(LayoutVerdict.NoStone, result.Verdict);
  }

  [Fact]
  public void Lay_WideBowTie_NamesNoCount() {
    // Laying stops at the guard, so the stones laid are no count of what the outline holds.
    var result = BreakwaterLayout.Lay(
      LayoutInputTests.Outlines["a wide bow-tie of little area"].Outline,
      FixtureStones.Crates,
      BreakwaterParameters.Default,
      LayoutInputTests.Slope
    );

    Assert.Equal($"more stones than the limit of {BreakwaterLayout.Guard}", result.WhyNothing);
  }

  [Fact]
  public void Lay_LongDiagonal_KeepsItsFieldUnderTheCap() {
    var result = GroundFixture.Layout(ProvingSites.Named("long-diagonal"));

    Assert.InRange(result.FieldCells, 1, ToeField.Cap);
  }

  [Fact]
  public void Lay_RandomOutlines_KeepTheInvariants() {
    // Rough ground: random outlines round the noise site, of 3 to 12 points in a square of 60 m,
    // crossing themselves as often as not.
    var site = ProvingSites.Named("noise");
    var centre = site.Outline.Aggregate(static (a, b) => a + b) / site.Outline.Length;
    var random = new Random(20261120u);
    var host = GroundFixture.Host;
    var verdicts = new Dictionary<LayoutVerdict, int>();

    for (var n = 0; n < LayoutInputTests.FuzzCount; n++) {
      var outline = Enumerable.Range(0, random.NextInt(3, 13))
        .Select(_ => centre + random.NextFloat2(-30f, 30f))
        .ToArray();

      var result = BreakwaterLayout.Lay(
        outline,
        FixtureStones.Boulders,
        BreakwaterParameters.Default,
        host
      );

      verdicts[result.Verdict] =
        verdicts.TryGetValue(result.Verdict, out var count) ? count + 1 : 1;

      Assert.InRange(result.Stones.Count, 0, BreakwaterLayout.Guard);
      Assert.True(result.Verdict is LayoutVerdict.Laid || result.Stones.Count == 0, $"outline {n}");
    }

    // Most of them lay stones: the fuzz is not only exercising the early exits.
    Assert.True(
      verdicts.GetValueOrDefault(LayoutVerdict.Laid) > LayoutInputTests.FuzzCount / 2,
      string.Join(", ", verdicts)
    );
  }
}
