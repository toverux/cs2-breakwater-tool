using System.Linq;
using BreakwaterTool.Layout;
using BreakwaterTool.Scenarios.Ground;
using BreakwaterTool.Tests.Scenarios.Ground;
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
  public void Lay_Site_KeepsTheInvariants(string name) {
    var site = ProvingSites.Named(name);

    Assert.Null(
      LayoutInvariants.Breach(
        site.Outline,
        GroundFixture.Layout(site),
        FixtureStones.For(site.Stones),
        site.Parameters
      )
    );
  }

  [Theory]
  [MemberData(nameof(ProvingSiteTests.Sites))]
  public void Lay_Site_LeavesNoHole(string name) {
    var site = ProvingSites.Named(name);
    var result = GroundFixture.Layout(site);

    if (result.Verdict is not LayoutVerdict.Laid) {
      return;
    }

    var (rooms, _) = LayoutInvariants.Barest(
      site.Outline,
      result,
      FixtureStones.For(site.Stones),
      site.Parameters
    );

    // Where rows of two sides meet at an angle, the places they lose would leave more.
    Assert.InRange(rooms, 0f, 1.3f);
  }

  [Theory]
  [MemberData(nameof(ProvingSiteTests.Sites))]
  public void Lay_Site_KeepsTheBrickBond(string name) {
    var site = ProvingSites.Named(name);
    var result = GroundFixture.Layout(site);

    if (result.Verdict is not LayoutVerdict.Laid ||
      result.Stones.Count > ProvingSiteTests.PictureLimit) {
      return;
    }

    var share = BrickBond.BrokenShare(
      site.Outline,
      result,
      FixtureStones.For(site.Stones),
      site.Parameters
    );

    Assert.InRange(share, 0f, BrickBond.MostBroken);
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
}
