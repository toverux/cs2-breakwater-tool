using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BreakwaterTool.Layout;
using Unity.Mathematics;

namespace BreakwaterTool.Scenarios.Ground;

/// <summary>
/// A layout seen from above, as an SVG picture: the outline, its toe, and each stone's footprint,
/// the rectangle it shows over the ground. One layer per layout, so that two can be compared.
/// A stone is rounder than its rectangle, so a picture is judged for its rows, bond, and turn; bare
/// ground is judged by the tests on gaps and in the game's album, where more of it shows.
/// </summary>
internal static class LayoutPicture {
  /// <summary>
  /// The picture's coordinates, to the centimetre.
  /// </summary>
  private const string Number = "0.##";

  /// <param name="outline">The breakwater's outline, in game metres.</param>
  /// <param name="toe">Per edge of the outline, whether it belongs to the toe.</param>
  /// <param name="layers">Each layer's class, which colours it, and its stones' footprints.</param>
  internal static string Render(
    IReadOnlyList<float2> outline,
    IReadOnlyList<bool> toe,
    params (string Class, IEnumerable<float2[]> Footprints)[] layers
  ) {
    var min = outline.Aggregate(math.min) - 5f;
    var max = outline.Aggregate(math.max) + 5f;
    var svg = new StringBuilder();

    // North up: the picture's y runs down, the game's z up.
    svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"")
      .Append(LayoutPicture.Format(min.x))
      .Append(' ')
      .Append(LayoutPicture.Format(-max.y))
      .Append(' ')
      .Append(LayoutPicture.Format(max.x - min.x))
      .Append(' ')
      .Append(LayoutPicture.Format(max.y - min.y))
      .Append("\">\n")
      .Append("<style>")
      .Append(".outline{fill:#dde8f0;stroke:#345;stroke-width:0.3}")
      .Append(".toe{fill:none;stroke:#c22;stroke-width:0.8}")
      .Append(".laid{fill:#9a948a;stroke:#333;stroke-width:0.06}")
      .Append(".other{fill:none;stroke:#06c;stroke-width:0.12}")
      .Append("</style>\n")
      .Append("<g transform=\"scale(1,-1)\">\n");

    svg.Append("<polygon class=\"outline\" points=\"")
      .Append(LayoutPicture.Points(outline))
      .Append("\"/>\n");

    for (var i = 0; i < outline.Count; i++) {
      if (toe[i]) {
        svg.Append("<polyline class=\"toe\" points=\"")
          .Append(LayoutPicture.Points([outline[i], outline[(i + 1) % outline.Count]]))
          .Append("\"/>\n");
      }
    }

    foreach (var layer in layers) {
      foreach (var footprint in layer.Footprints) {
        svg.Append("<polygon class=\"")
          .Append(layer.Class)
          .Append("\" points=\"")
          .Append(LayoutPicture.Points(footprint))
          .Append("\"/>\n");
      }
    }

    return svg.Append("</g>\n</svg>\n").ToString();
  }

  /// <summary>
  /// Each stone's footprint: the corners of the rectangle its shape shows at the embed line, where
  /// the stone lies.
  /// </summary>
  internal static IEnumerable<float2[]> Footprints(
    IEnumerable<LaidStone> stones,
    IReadOnlyList<StoneShape> shapes,
    float embed
  ) =>
    stones.Select(stone => LayoutPicture.Corners(stone, shapes[stone.Shape], embed));

  /// <summary>
  /// The middle of a stone's footprint on the ground, where the layout placed it.
  /// </summary>
  internal static float2 Centre(LaidStone stone, StoneShape shape, float embed) {
    shape.Footprint(embed, out var centre);

    return LayoutPicture.Place(stone, shape, new float3(centre.x, 0f, centre.y));
  }

  private static float2[] Corners(LaidStone stone, StoneShape shape, float embed) {
    var size = shape.Footprint(embed, out var centre);
    var half = size / 2f;

    return [
      LayoutPicture.Place(stone, shape, new float3(centre.x - half.x, 0f, centre.y - half.y)),
      LayoutPicture.Place(stone, shape, new float3(centre.x + half.x, 0f, centre.y - half.y)),
      LayoutPicture.Place(stone, shape, new float3(centre.x + half.x, 0f, centre.y + half.y)),
      LayoutPicture.Place(stone, shape, new float3(centre.x - half.x, 0f, centre.y + half.y))
    ];
  }

  /// <summary>
  /// A point of the shape's long axis frame where the stone puts it, seen from above. The stone's
  /// rotation applies to the prefab's frame, the long axis frame turned back by the shape's angle.
  /// </summary>
  private static float2 Place(LaidStone stone, StoneShape shape, float3 point) {
    var frame = math.mul(stone.Rotation, quaternion.RotateY(-shape.Angle));

    return (stone.Position + math.rotate(frame, point)).xz;
  }

  private static string Points(IEnumerable<float2> points) =>
    string.Join(
      " ",
      points.Select(static point =>
        $"{LayoutPicture.Format(point.x)},{LayoutPicture.Format(point.y)}"
      )
    );

  private static string Format(float value) =>
    value.ToString(LayoutPicture.Number, CultureInfo.InvariantCulture);
}
