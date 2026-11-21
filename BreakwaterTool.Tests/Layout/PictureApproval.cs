using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace BreakwaterTool.Tests.Layout;

/// <summary>
/// Approves an SVG picture against the one committed in <c>Pictures/</c> beside the test, its
/// coordinates within a tolerance.
/// A layout's decisions are deterministic per runtime, not across: where a float flips one between
/// the passes, the float64 pass has its own picture, <c>name.float64.svg</c>.
/// </summary>
internal static class PictureApproval {
  // Read, not constant, so that each pass compiles the other's branch.
  #if FLOAT64
  private static readonly bool IsFloat64Pass = true;
  #else
  private static readonly bool IsFloat64Pass = false;
  #endif

  /// <summary>
  /// How far a coordinate may move and the picture still match, in metres: the two float passes
  /// round a few to the next centimetre.
  /// </summary>
  private const float Tolerance = 0.015f;

  private static readonly Regex Number = new(@"-?\d+(\.\d+)?", RegexOptions.Compiled);

  internal static void Verify(string name, string picture, [CallerFilePath] string source = "") {
    var shared = $"Pictures/{name}.svg";
    var own = $"Pictures/{name}.float64.svg";
    var folder = Path.GetDirectoryName(source)!;

    if (!PictureApproval.IsFloat64Pass) {
      Approval.Verify(shared, picture, PictureApproval.Matches, source);

      return;
    }

    // Without a picture of its own, the float64 pass matching the shared one passes; failing, it
    // receives its picture as its own, to approve beside the shared one.
    if (!File.Exists(Path.Combine(folder, own)) &&
      File.Exists(Path.Combine(folder, shared)) &&
      PictureApproval.Matches(File.ReadAllText(Path.Combine(folder, shared)), picture)) {
      return;
    }

    Approval.Verify(own, picture, PictureApproval.Matches, source);
  }

  /// <summary>
  /// Whether two pictures have the same elements, their numbers within the tolerance.
  /// </summary>
  private static bool Matches(string approved, string received) {
    if (PictureApproval.Number.Replace(approved, "#") !=
      PictureApproval.Number.Replace(received, "#")) {
      return false;
    }

    return PictureApproval.Number.Matches(approved)
      .Zip(PictureApproval.Number.Matches(received))
      .All(static pair =>
        Math.Abs(PictureApproval.Parse(pair.First) - PictureApproval.Parse(pair.Second)) <=
        PictureApproval.Tolerance
      );
  }

  private static float Parse(Match number) =>
    float.Parse(number.Value, CultureInfo.InvariantCulture);
}
