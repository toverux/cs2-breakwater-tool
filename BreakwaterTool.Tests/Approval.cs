using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace BreakwaterTool.Tests;

/// <summary>
/// Approves a text against the file committed beside the test that produced it.
/// A text that differs is written beside the approved file, <c>.received</c> before its extension,
/// and fails the test: replace the approved file with it to approve the change.
/// </summary>
internal static class Approval {
  /// <param name="path">The approved file, from the folder of the calling test's source.</param>
  /// <param name="received">The text the test produced.</param>
  /// <param name="matches">
  /// Whether the approved text and the received one match; equal by default.
  /// </param>
  internal static void Verify(
    string path,
    string received,
    Func<string, string, bool>? matches = null,
    [CallerFilePath] string source = ""
  ) {
    var approved = Path.Combine(Path.GetDirectoryName(source)!, path);
    var receivedPath = Path.ChangeExtension(approved, $".received{Path.GetExtension(approved)}");

    if (File.Exists(approved) && (matches ?? string.Equals)(File.ReadAllText(approved), received)) {
      File.Delete(receivedPath);

      return;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(approved)!);
    File.WriteAllText(receivedPath, received);

    Assert.Fail(
      $"{path} is new or differs from the approved one: compare {receivedPath} with {approved}, " +
      "and replace it to approve the change."
    );
  }
}
