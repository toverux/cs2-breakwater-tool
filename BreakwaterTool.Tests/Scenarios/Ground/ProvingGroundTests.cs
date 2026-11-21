using System.Linq;
using BreakwaterTool.Scenarios.Ground;
using Unity.Mathematics;
using Xunit;

namespace BreakwaterTool.Tests.Scenarios.Ground;

public sealed class ProvingGroundTests {
  [Fact]
  public void HeightData_ScalesAndOffsetsAsTheGame() {
    var data = ProvingGround.HeightData(GroundFixture.Heights);

    // As the game gives them, read on "Nice 22" but for the height scale: posts 3.5 m apart, the
    // first one's middle 1.75 m in from the playable area's edge.
    Assert.Equal(new int3(4096, 65536, 4096), data.resolution);
    Assert.Equal(0.2857143f, data.scale.x, 1e-7f);
    Assert.Equal(65535f / 256f, data.scale.y, 1e-3f);
    Assert.Equal(7166.25f, data.offset.x, 1e-3f);
    Assert.Equal(7166.25f, data.offset.z, 1e-3f);
  }

  [Theory]
  [InlineData("lympia", 101.877f)]
  [InlineData("user-hook", 94.454f)]
  public void Heights_PatchLaysTheNiceGroundOnTheSea(string name, float niceHeight) {
    // The height the game sampled on "Nice 22" at the outline's first point. The patch keeps the
    // ground under its outline, its sea moved from 99.95 m to the ground's: 5107 steps of 256 m.
    var site = ProvingSites.Named(name);
    var host = GroundFixture.Host;

    Assert.Equal(niceHeight - (5107f * 256f / 65535f), host.HeightAt(site.Outline[0]), 1e-3f);
  }

  [Fact]
  public void Sites_EachHoldsItsOutlineInAPlotOfItsOwn() {
    // Room past the outline for the distance field's margin and the tilt read round a stone.
    const float room = 15f;

    var sites = ProvingSites.All;

    Assert.All(
      sites,
      site => {
        var low = new float2(
          (float) ProvingGround.World(site.FirstPost.x),
          (float) ProvingGround.World(site.FirstPost.y)
        );

        var high = new float2(
          (float) ProvingGround.World(site.FirstPost.x + site.Posts.x - 1),
          (float) ProvingGround.World(site.FirstPost.y + site.Posts.y - 1)
        );

        Assert.True(math.all(site.FirstPost >= 0), site.Name);
        Assert.True(math.all(site.FirstPost + site.Posts <= ProvingGround.Resolution), site.Name);
        Assert.All(site.Outline, point => Assert.True(math.all(point - room >= low), site.Name));
        Assert.All(site.Outline, point => Assert.True(math.all(point + room <= high), site.Name));
      }
    );

    var plots = sites.OrderBy(static site => site.FirstPost.x).ToArray();

    for (var i = 1; i < plots.Length; i++) {
      Assert.True(plots[i].FirstPost.x >= plots[i - 1].FirstPost.x + plots[i - 1].Posts.x);
    }
  }

  [Fact]
  public void Heights_AreTheSameGroundInBothFloatPasses() {
    // Pinned on the first run: a change of the ground changes it, a pass that rounds a height
    // otherwise breaks it.
    Assert.Equal(4157156818u, ProvingGround.Digest(GroundFixture.Heights));
  }
}
