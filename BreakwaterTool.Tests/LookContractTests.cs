using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BreakwaterTool.Breakwaters;
using Xunit;

namespace BreakwaterTool.Tests;

/// <summary>
/// What the panel and the source locale must hold for each parameter and style the C# side names:
/// the two sides share names that no compiler checks.
/// </summary>
public sealed class LookContractTests {
  private static readonly string Bindings = LookContractTests.Read(
    "BreakwaterTool/UI/src/utils/bindings/breakwater-tool.ts"
  );

  private static readonly string Locale = LookContractTests.Read(
    "BreakwaterTool/Locales/en-US.json"
  );

  [Fact]
  public void ParameterName_InTheUI_ListsTheParameters() {
    var union = Regex.Match(
      LookContractTests.Bindings,
      @"export type ParameterName =(?<members>(\s*\| '\w+')+);"
    );

    Assert.True(union.Success);
    Assert.Equal(
      ParameterTable.Names,
      Regex.Matches(union.Groups["members"].Value, @"'(\w+)'").Select(static m => m.Groups[1].Value)
    );
  }

  [Fact]
  public void SourceLocale_NamesEachParameterThePanelShows() {
    // The seed has no row: Reshuffle draws another.
    Assert.All(
      ParameterTable.Names.Except(["Seed"]),
      static name =>
        Assert.Contains($"\"BreakwaterTool.Tool.PARAMETER[{name}]\"", LookContractTests.Locale)
    );
  }

  [Fact]
  public void SourceLocale_SaysWhatEachParameterThePanelShowsDoes() {
    // What a row's tooltip says, beside the panel.
    Assert.All(
      ParameterTable.Names.Except(["Seed"]),
      static name => Assert.Contains(
        $"\"BreakwaterTool.Tool.PARAMETER_DESCRIPTION[{name}]\"",
        LookContractTests.Locale
      )
    );
  }

  [Theory]
  [InlineData(BreakwaterToolSystem.SetToeLabel)]
  [InlineData(BreakwaterToolSystem.RemoveToeLabel)]
  public void SourceLocale_NamesEachLabelOfTheHintsBar(string label) =>
    Assert.Contains($"\"Common.ACTION[{label}]\"", LookContractTests.Locale);

  [Fact]
  public void SourceLocale_NamesEachStyle() {
    Assert.All(
      BreakwaterStyles.Names,
      static name =>
        Assert.Contains($"\"BreakwaterTool.Style.NAME[{name}]\"", LookContractTests.Locale)
    );
  }

  private static string Read(string path, [CallerFilePath] string source = "") =>
    File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, "..", path));
}
