using System.Collections.Generic;
using System.Linq;
using BreakwaterTool.Layout;
using BreakwaterTool.Scenarios.Ground;
using BreakwaterTool.Tests.Scenarios.Ground;
using Unity.Mathematics;
using Xunit;

namespace BreakwaterTool.Tests.Layout;

/// <summary>
/// The layout's invariants on every site of the proving ground, and each site's picture.
/// </summary>
public sealed class ProvingSiteTests {
  /// <summary>
  /// The most stones a picture is drawn for: past it, a site is about scale, not looks.
  /// </summary>
  private const int PictureLimit = 5000;

  public static TheoryData<string> Sites { get; } = [
    .. ProvingSites.All.Select(static site => site.Name)
  ];

  [Theory]
  [MemberData(nameof(ProvingSiteTests.Sites))]
  public void Lay_Site_ReachesItsVerdict(string name) {
    var site = ProvingSites.Named(name);

    Assert.Equal(site.Verdict, GroundFixture.Layout(site).Verdict);
  }

  [Theory]
  [MemberData(nameof(ProvingSiteTests.Sites))]
  public void Lay_Site_LaysNoMoreThanTheGuard(string name) {
    var result = GroundFixture.Layout(ProvingSites.Named(name));

    Assert.InRange(result.Stones.Count, 0, BreakwaterLayout.Guard);
  }

  [Theory]
  [MemberData(nameof(ProvingSiteTests.Sites))]
  public void Lay_Site_KeepsEveryStoneInside(string name) {
    var site = ProvingSites.Named(name);
    var result = GroundFixture.Layout(site);
    var shapes = FixtureStones.For(site.Stones);

    // A stone sits on a contour of the distance field, inside, up to the field's interpolation
    // over one cell, and moved by its irregularity, up to that share of a spacing each way.
    var cell = math.min(0.5f, math.min(result.StoneSpacing, result.RowSpacing) / 4f);
    var tolerance = cell + (site.Parameters.Irregularity * result.StoneSpacing * math.SQRT2);

    var outside = result.Stones
      .Select(stone => LayoutPicture.Centre(stone, shapes[stone.Shape], site.Parameters.Embed))
      .Select(centre => ProvingSiteTests.Outside(centre, site.Outline))
      .DefaultIfEmpty(0f)
      .Max();

    Assert.InRange(outside, 0f, tolerance);
  }

  [Theory]
  [MemberData(nameof(ProvingSiteTests.Sites))]
  public void Lay_Site_LaysTheSameStonesAgain(string name) {
    var site = ProvingSites.Named(name);

    Assert.Equal(GroundFixture.Layout(site).Stones, GroundFixture.Lay(site).Stones);
  }

  [Theory]
  [MemberData(nameof(ProvingSiteTests.Sites))]
  public void Lay_Site_MatchesItsApprovedPicture(string name) {
    var site = ProvingSites.Named(name);
    var result = GroundFixture.Layout(site);

    if (result.Verdict is not LayoutVerdict.Laid ||
      result.Stones.Count > ProvingSiteTests.PictureLimit) {
      return;
    }

    var footprints = LayoutPicture.Footprints(
      result.Stones,
      FixtureStones.For(site.Stones),
      site.Parameters.Embed
    );

    PictureApproval.Verify(
      name,
      LayoutPicture.Render(site.Outline, result.ToeEdges, ("laid", footprints))
    );
  }

  /// <summary>
  /// How far a point lies outside a polygon, or zero inside it.
  /// </summary>
  private static float Outside(float2 point, IReadOnlyList<float2> polygon) {
    var inside = false;
    var nearest = float.MaxValue;

    for (var i = 0; i < polygon.Count; i++) {
      var a = polygon[i];
      var b = polygon[(i + 1) % polygon.Count];
      var ab = b - a;
      var t = math.saturate(math.dot(point - a, ab) / math.lengthsq(ab));

      nearest = math.min(nearest, math.distance(point, a + (t * ab)));

      if ((a.y > point.y) != (b.y > point.y) &&
        point.x < a.x + ((point.y - a.y) / (b.y - a.y) * ab.x)) {
        inside = !inside;
      }
    }

    return inside ? 0f : nearest;
  }
}
