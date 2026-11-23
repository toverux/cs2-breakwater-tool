using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace BreakwaterTool.Layout;

/// <summary>
/// The places of a layer: the ones taken, and the ones refused for standing too near them.
/// </summary>
/// <param name="gap">The most that two places may have to be apart.</param>
/// <param name="room">
/// The most room between the places of an even bond: how far from them its barest spot is.
/// </param>
internal sealed class PlaceGrid(float gap, float room) {
  /// <summary>
  /// The room that makes a spot a hole, in rooms of the even bond: enough over it that a row a
  /// little off the bond holds none.
  /// </summary>
  private const float Hole = 1.1f;

  /// <summary>
  /// How far round a refused place the taken ones are read, in rooms: to a room and a half past the
  /// spots weighed.
  /// </summary>
  private const float Sight = 2.5f;

  /// <summary>
  /// How many spots round a refused place are weighed.
  /// </summary>
  private const int SpotCount = 48;

  /// <summary>
  /// The turn, in radians, from one spot of a spiral to the next that spreads them evenly.
  /// </summary>
  private const float GoldenAngle = 2.3999632f;

  /// <summary>
  /// The spots weighed, from the refused place, in rooms: a spiral that fills a circle of one.
  /// </summary>
  // [PERF] Placed once, in place of round each refused place.
  private static readonly float2[] Spots = [
    .. Enumerable.Range(0, PlaceGrid.SpotCount)
      .Select(static i => {
          math.sincos(i * PlaceGrid.GoldenAngle, out var sin, out var cos);

          return math.sqrt((i + 0.5f) / PlaceGrid.SpotCount) * new float2(cos, sin);
        }
      )
  ];

  private readonly SpatialIndex taken = new(gap);

  private readonly List<RowPlace> refused = [];

  private readonly List<float2> near = [];

  /// <summary>
  /// Takes a place, unless one already taken is nearer than a distance, a gap at most, which leaves
  /// it refused.
  /// </summary>
  internal bool TryTake(RowPlace place, float least) {
    this.taken.Near(place.Position, this.near);

    if (this.SquaredRoom(place.Position) < least * least) {
      this.refused.Add(place);

      return false;
    }

    this.taken.Add(place.Position);

    return true;
  }

  /// <summary>
  /// Takes the roomiest spot round each refused place that is a hole: rows that meet at an angle
  /// leave one beside the places they lose.
  /// </summary>
  internal List<RowPlace> TakeHoles(ToeField field) {
    var holes = new List<RowPlace>();

    if (this.refused.Count == 0) {
      return holes;
    }

    // [PERF] Its own index, which reaches farther, in place of a far reach for every place taken.
    var index = new SpatialIndex(PlaceGrid.Sight * room);

    foreach (var point in this.taken.Points) {
      index.Add(point);
    }

    foreach (var place in this.refused) {
      // [PERF] Read once for all the spots round the place, in place of once a spot.
      index.Near(place.Position, this.near);

      if (this.HoleBy(place.Position, field) is not {} hole) {
        continue;
      }

      index.Add(hole);

      holes.Add(
        place with {
          Position = hole
        }
      );
    }

    return holes;
  }

  /// <summary>
  /// The hole with the most room within a room of a point, in the outline, from the places read
  /// round it.
  /// </summary>
  private float2? HoleBy(float2 point, ToeField field) {
    float2? hole = null;
    var most = PlaceGrid.Hole * PlaceGrid.Hole * room * room;

    foreach (var offset in PlaceGrid.Spots) {
      var spot = point + (room * offset);
      var spotRoom = this.SquaredRoom(spot);

      if (spotRoom > most && field.Contains(spot)) {
        hole = spot;
        most = spotRoom;
      }
    }

    return hole;
  }

  /// <summary>
  /// How far from a point the nearest of the places read is, squared.
  /// </summary>
  private float SquaredRoom(float2 point) {
    var nearest = float.PositiveInfinity;

    // [PERF] Spelled out and squared, in place of math.distance, a call Mono does not inline.
    foreach (var place in this.near) {
      var offsetX = place.x - point.x;
      var offsetZ = place.y - point.y;
      var squared = (offsetX * offsetX) + (offsetZ * offsetZ);

      if (squared < nearest) {
        nearest = squared;
      }
    }

    return nearest;
  }
}
