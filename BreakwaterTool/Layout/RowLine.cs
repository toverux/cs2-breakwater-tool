using Unity.Mathematics;

namespace BreakwaterTool.Layout;

/// <summary>
/// A line a row of stones follows: the toe, or a contour of the distance to it.
/// </summary>
/// <param name="Points">The line's points in order; a closed one does not repeat its first.</param>
/// <param name="IsClosed">Whether the line joins its own end.</param>
internal readonly record struct RowLine(float2[] Points, bool IsClosed) {
  /// <summary>
  /// The line's length from its first point to its last.
  /// </summary>
  internal float Length {
    get {
      var length = 0f;

      for (var i = 1; i < this.Points.Length; i++) {
        length += math.distance(this.Points[i - 1], this.Points[i]);
      }

      return length;
    }
  }
}

/// <summary>
/// A stone's place on its row: where it goes, and the direction of the row there.
/// </summary>
internal readonly record struct RowPlace(float2 Position, float2 Along);
