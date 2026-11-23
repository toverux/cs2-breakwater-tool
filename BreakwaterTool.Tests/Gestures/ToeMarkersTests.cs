using System.Linq;
using BreakwaterTool.Gestures;
using Unity.Mathematics;
using Xunit;

namespace BreakwaterTool.Tests.Gestures;

public sealed class ToeMarkersTests {
  /// <summary>
  /// Its edges, in order: z = 0, x = 10, z = 10 and x = 0.
  /// </summary>
  private static readonly float2[] Square = [
    new(0f, 0f), new(10f, 0f), new(10f, 10f), new(0f, 10f)
  ];

  [Fact]
  public void Of_AnOutline_FloatsAMarkerOutsideTheMiddleOfEachEdge() {
    var markers = ToeMarkers.Of(ToeMarkersTests.Square, static _ => 0f, static _ => 1f);

    // 2.5 reaches outside each edge, a reach in radius.
    Assert.Equal(
      [
        new ToeMarker(new float3(5f, 0f, -2.5f), 1f),
        new ToeMarker(new float3(12.5f, 0f, 5f), 1f),
        new ToeMarker(new float3(5f, 0f, 12.5f), 1f),
        new ToeMarker(new float3(-2.5f, 0f, 5f), 1f)
      ],
      markers
    );
  }

  [Fact]
  public void Of_AnOutlineTurningTheOtherWay_StillFloatsTheMarkersOutside() {
    float2[] turned = [.. ToeMarkersTests.Square.Reverse()];

    var markers = ToeMarkers.Of(turned, static _ => 0f, static _ => 1f);

    // The first edge now runs from (0, 10) to (10, 10), along z = 10.
    Assert.Equal(new float3(5f, 0f, 12.5f), markers[0].At);
    Assert.All(markers, static marker => Assert.False(ToeMarkersTests.IsInside(marker.At.xz)));
  }

  [Fact]
  public void Of_AFartherView_SizesAMarkerByThePointersReachAtItsEdge() {
    // The reach doubles past x = 8, where the middle of the edge x = 10 lies.
    var markers = ToeMarkers.Of(
      ToeMarkersTests.Square,
      static _ => 0f,
      static at => at.x > 8f ? 2f : 1f
    );

    Assert.Equal(new ToeMarker(new float3(15f, 0f, 5f), 2f), markers[1]);
    Assert.Equal(new ToeMarker(new float3(5f, 0f, -2.5f), 1f), markers[0]);
  }

  [Theory]
  // The ground outside the edge z = 0 stands above the edge, then drops under it: the marker sits
  // on the higher of the two.
  [InlineData(4f, 4f)]
  [InlineData(-4f, 0f)]
  public void Of_GroundOutsideAnEdge_KeepsTheMarkerOnOrAboveIt(float outside, float height) {
    var markers = ToeMarkers.Of(
      ToeMarkersTests.Square,
      at => at.y < -1f ? outside : 0f,
      static _ => 1f
    );

    Assert.Equal(new float3(5f, height, -2.5f), markers[0].At);
  }

  [Fact]
  public void Of_AnEdgeBetweenTwoHeights_FloatsTheMarkerAtTheEdgesMiddle() {
    // The ground climbs with x: the edge z = 0 runs from 0 m to 10 m, and the ground beside its
    // middle is as high as the middle.
    var markers = ToeMarkers.Of(ToeMarkersTests.Square, static at => at.x, static _ => 1f);

    Assert.Equal(new float3(5f, 5f, -2.5f), markers[0].At);
  }

  [Fact]
  public void Aimed_ARayThroughAMarker_FindsIt() {
    ToeMarker[] markers = [new(new float3(0f, 0f, 0f), 1f), new(new float3(10f, 0f, 0f), 1f)];

    Assert.Equal(1, ToeMarkers.Aimed(markers, new float3(10f, 50f, 0f), new float3(10.5f, 0f, 0f)));
  }

  [Fact]
  public void Aimed_ARayThatMeetsTheGroundAboveAMarker_StillFindsIt() {
    // The marker lies 5 m under the ground the ray stops on: it answers where it shows.
    ToeMarker[] markers = [new(new float3(0f, -5f, 0f), 1f)];

    Assert.Equal(0, ToeMarkers.Aimed(markers, new float3(0f, 50f, 0f), new float3(0f, 0f, 0f)));
  }

  [Fact]
  public void Aimed_ARayPassingAMarkerPastItsRadius_FindsNone() {
    ToeMarker[] markers = [new(new float3(0f, 0f, 0f), 1f)];

    Assert.Equal(
      -1,
      ToeMarkers.Aimed(markers, new float3(1.1f, 50f, 0f), new float3(1.1f, 0f, 0f))
    );
  }

  [Fact]
  public void Aimed_ARayWithinTwoMarkers_FindsTheOneItPassesNearest() {
    ToeMarker[] markers = [new(new float3(0f, 0f, 0f), 1f), new(new float3(1f, 0f, 0f), 1f)];

    Assert.Equal(1, ToeMarkers.Aimed(markers, new float3(0.7f, 50f, 0f), new float3(0.7f, 0f, 0f)));
    Assert.Equal(0, ToeMarkers.Aimed(markers, new float3(0.3f, 50f, 0f), new float3(0.3f, 0f, 0f)));
  }

  [Fact]
  public void Aimed_AMarkerBehindTheEye_FindsNone() {
    ToeMarker[] markers = [new(new float3(0f, 60f, 0f), 1f)];

    Assert.Equal(-1, ToeMarkers.Aimed(markers, new float3(0f, 50f, 0f), new float3(0f, 0f, 0f)));
  }

  [Fact]
  public void Aimed_NoMarker_FindsNone() =>
    Assert.Equal(-1, ToeMarkers.Aimed([], new float3(0f, 50f, 0f), new float3(0f, 0f, 0f)));

  private static bool IsInside(float2 at) => at is { x: > 0f and < 10f, y: > 0f and < 10f };
}
