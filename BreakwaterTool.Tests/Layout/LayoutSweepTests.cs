using System.Collections.Generic;
using System.Linq;
using BreakwaterTool.Layout;
using BreakwaterTool.Scenarios.Ground;
using Unity.Mathematics;
using Xunit;
using Random = Unity.Mathematics.Random;

namespace BreakwaterTool.Tests.Layout;

/// <summary>
/// The layout's invariants on outlines generated from fixed seeds, each laid by parameters of its
/// own: banks with notches and inlets cut into the toe and the crest, waists where the two meet,
/// toes split into several runs, on ground falling at any slope, level ground and cross slopes
/// included.
/// </summary>
public sealed class LayoutSweepTests {
  public static TheoryData<uint>
    Seeds { get; } = [.. Enumerable.Range(1, 60).Select(static i => (uint) i)];

  [Theory]
  [MemberData(nameof(LayoutSweepTests.Seeds))]
  public void Lay_GeneratedOutline_KeepsTheInvariants(uint seed) {
    var ground = LayoutSweepTests.Generate(seed);
    var result = ground.Lay(layers: 1);

    Assert.Equal(LayoutVerdict.Laid, result.Verdict);

    ground.AssertKeepsTheInvariants(result);
  }

  [Theory]
  [MemberData(nameof(LayoutSweepTests.Seeds))]
  public void Lay_GeneratedOutline_KeepsTheBrickBond(uint seed) {
    var ground = LayoutSweepTests.Generate(seed);
    var result = ground.Lay(layers: 1);

    // Rows farther apart fan out by more than a stone a turn, so there is no bond to keep.
    if (result.RowSpacing > 3f * result.StoneSpacing) {
      return;
    }

    var share = BrickBond.BrokenShare(ground.Outline, result, ground.Stones, ground.Parameters);

    Assert.InRange(share, 0f, BrickBond.MostBroken);
  }

  [Theory]
  [MemberData(nameof(LayoutSweepTests.Seeds))]
  public void Lay_SecondLayer_KeepsTheInvariantsToo(uint seed) {
    var ground = LayoutSweepTests.Generate(seed);
    var first = ground.Lay(layers: 1);
    var both = ground.Lay(layers: 2);

    Assert.Equal(first.Stones, both.Stones.Take(first.Stones.Count));

    // Without its toe edges: its rows start half a row off the toe.
    ground.AssertKeepsTheInvariants(
      new LayoutResult {
        Verdict = both.Verdict,
        Stones = [.. both.Stones.Skip(first.Stones.Count)],
        StoneSpacing = both.StoneSpacing,
        RowSpacing = both.RowSpacing
      }
    );
  }

  /// <summary>
  /// A bank, u along the toe and v up the slope, turned and moved over the plan.
  /// </summary>
  private static Ground Generate(uint seed) {
    var random = new Random((seed * 2654435761u) + 1u);

    // Banks of crates are small, of boulders large, so that both hold a few hundred stones.
    var boulders = random.NextBool();
    var scale = boulders ? 2.4f : 1f;
    var width = random.NextFloat(30f, 60f) * scale;
    var depth = random.NextFloat(9f, 18f) * scale;

    // Level ground one time in eight: every edge is a toe.
    var slope = random.NextInt(8) == 0 ? 0f : random.NextFloat(0.15f, 0.8f);
    var tilt = slope == 0f ? 0f : random.NextFloat(-0.1f, 0.1f);

    var bottom = LayoutSweepTests.Cuts(ref random, new float2(width, depth), scale, 0.7f);
    var top = LayoutSweepTests.Cuts(ref random, new float2(width, depth), scale, 0.3f);

    var points = new List<float2> {
      new(0f, 0f)
    };

    foreach (var cut in bottom) {
      points.AddRange(cut.Points(0f, 1f));
    }

    points.Add(new float2(width, 0f));
    points.Add(new float2(width, depth));

    foreach (var cut in Enumerable.Reverse(top)) {
      points.AddRange(cut.Points(depth, -1f).Reverse());
    }

    points.Add(new float2(0f, depth));

    var angle = random.NextFloat(0f, 2f * math.PI);
    var shift = random.NextFloat2(-200f, 200f);

    // Jittered, so that no edge is quite straight up the slope.
    var outline = new float2[points.Count];

    for (var i = 0; i < outline.Length; i++) {
      var jitter = random.NextFloat2(-0.25f, 0.25f) * scale;

      outline[i] = LayoutSweepTests.Plan(points[i] + jitter, angle, shift);
    }

    return new Ground {
      Seed = seed,
      Parameters = LayoutSweepTests.Parameters(ref random, seed),
      Outline = outline,
      Stones = boulders ? FixtureStones.Boulders : FixtureStones.Crates,
      Host = new FixtureHost(xz => {
          var local = LayoutSweepTests.Local(xz, angle, shift);

          return (slope * local.y) + (tilt * local.x);
        }
      )
    };
  }

  /// <summary>
  /// Stones from a third to three times as dense and as close across the rows as the defaults, laid
  /// along or across.
  /// </summary>
  private static BreakwaterParameters Parameters(ref Random random, uint seed) =>
    BreakwaterParameters.Default with {
      Seed = seed,
      Density = BreakwaterParameters.Default.Density * math.pow(3f, random.NextFloat(-1f, 1f)),
      RowRatio = BreakwaterParameters.Default.RowRatio * math.pow(3f, random.NextFloat(-1f, 1f)),
      IsLongAxisAcross = random.NextBool()
    };

  /// <summary>
  /// Up to three cuts along an edge of the bank, clear of its ends and of each other.
  /// </summary>
  /// <param name="bank">The bank's width along the edge and its depth.</param>
  private static List<Cut> Cuts(ref Random random, float2 bank, float scale, float reach) {
    var (width, depth) = (bank.x, bank.y);
    var cuts = new List<Cut>();
    var at = random.NextFloat(0.1f, 0.3f) * width;
    var count = random.NextInt(0, 4);

    for (var i = 0; i < count; i++) {
      // Room left for this cut and the far end.
      var most = math.min(0.3f * width, (0.95f * width) - at);

      if (most < 4f * scale) {
        break;
      }

      var size = random.NextFloat(3f * scale, most);

      cuts.Add(
        new Cut {
          At = at,
          Width = size,
          Depth = random.NextFloat(0.2f, reach) * depth,
          IsVee = random.NextBool()
        }
      );

      at += size + (random.NextFloat(0.06f, 0.2f) * width);
    }

    return cuts;
  }

  private static float2 Plan(float2 point, float angle, float2 shift) {
    math.sincos(angle, out var sin, out var cos);

    return new float2((point.x * cos) - (point.y * sin), (point.x * sin) + (point.y * cos)) + shift;
  }

  private static float2 Local(float2 point, float angle, float2 shift) {
    math.sincos(-angle, out var sin, out var cos);

    var moved = point - shift;

    return new float2((moved.x * cos) - (moved.y * sin), (moved.x * sin) + (moved.y * cos));
  }

  private sealed class Ground {
    internal required uint Seed { get; init; }

    internal required BreakwaterParameters Parameters { get; init; }

    internal required float2[] Outline { get; init; }

    internal required IReadOnlyList<StoneShape> Stones { get; init; }

    internal required ILayoutHost Host { get; init; }

    internal LayoutResult Lay(int layers) =>
      BreakwaterLayout.Lay(
        this.Outline,
        this.Stones,
        this.Parameters with {
          Layers = layers
        },
        this.Host
      );

    internal void AssertKeepsTheInvariants(LayoutResult result) {
      var breach = LayoutInvariants.Breach(this.Outline, result, this.Stones, this.Parameters);

      Assert.True(
        breach is null,
        $"Seed {this.Seed}: {breach} {this.Parameters} Outline: " +
        string.Join(" ", this.Outline.Select(static point => $"{point.x:R},{point.y:R}"))
      );
    }
  }

  private sealed class Cut {
    internal required float At { get; init; }

    internal required float Width { get; init; }

    internal required float Depth { get; init; }

    internal required bool IsVee { get; init; }

    /// <summary>
    /// The corners of the cut into an edge of the bank at v, going into the bank by a sign.
    /// </summary>
    internal IEnumerable<float2> Points(float v, float into) {
      var left = this.At;
      var right = this.At + this.Width;
      var end = v + (into * this.Depth);

      yield return new float2(left, v);

      if (this.IsVee) {
        yield return new float2((left + right) / 2f, end);
      }
      else {
        yield return new float2(left, end);
        yield return new float2(right, end);
      }

      yield return new float2(right, v);
    }
  }
}
