using System.Linq;
using Xunit;

namespace BreakwaterTool.Tests;

public sealed class ParameterTableTests {
  /// <summary>
  /// Every parameter off its default, the numbers no float holds exactly among them.
  /// </summary>
  private static readonly BreakwaterParameters Unusual = new() {
    Density = 2.37f,
    Embed = 0.61f,
    Irregularity = 0.07f,
    Seed = 4_000_000_000,
    RowRatio = 0.83f,
    LevelTops = 0.31f,
    IsLongAxisAcross = true,
    FollowSlope = 0.72f,
    TurnJitter = 12.5f,
    SizeTolerance = 0.21f,
    Mixing = 0.43f,
    Layers = 2,
    UnderLayerDepth = 0.53f,
    Tumble = 0.11f,
    ToeShare = 0.33f,
    LevelHeight = 3.1f
  };

  [Fact]
  public void Names_AreTheParametersInTheirOrder() {
    // The names the panel sends, the look's binding carries and the settings keep.
    Assert.Equal(
      [
        "Density",
        "Embed",
        "Irregularity",
        "Seed",
        "RowRatio",
        "LevelTops",
        "IsLongAxisAcross",
        "FollowSlope",
        "TurnJitter",
        "SizeTolerance",
        "Mixing",
        "Layers",
        "UnderLayerDepth",
        "Tumble",
        "ToeShare",
        "LevelHeight"
      ],
      ParameterTable.Names
    );
  }

  [Fact]
  public void Get_EachKind_GivesItAsANumber() {
    Assert.Equal(2.37f, (float) ParameterTable.Get(ParameterTableTests.Unusual, "Density"));
    Assert.Equal(4_000_000_000d, ParameterTable.Get(ParameterTableTests.Unusual, "Seed"));
    Assert.Equal(2d, ParameterTable.Get(ParameterTableTests.Unusual, "Layers"));
    Assert.Equal(1d, ParameterTable.Get(ParameterTableTests.Unusual, "IsLongAxisAcross"));
    Assert.Equal(0d, ParameterTable.Get(BreakwaterParameters.Default, "IsLongAxisAcross"));
  }

  [Fact]
  public void With_AParameter_ChangesItAloneOnACopy() {
    var changed = ParameterTable.With(BreakwaterParameters.Default, "Embed", 0.25);

    Assert.Equal(
      BreakwaterParameters.Default with {
        Embed = 0.25f
      },
      changed
    );

    Assert.Equal(0.5f, BreakwaterParameters.Default.Embed);
  }

  [Theory]
  [InlineData("IsLongAxisAcross", 1d)]
  [InlineData("Layers", 2d)]
  [InlineData("Seed", 4_000_000_000d)]
  [InlineData("TurnJitter", 180d)]
  public void With_EachKind_ReadsBackWhatWasSet(string name, double value) {
    var changed = ParameterTable.With(BreakwaterParameters.Default, name, value);

    Assert.NotNull(changed);
    Assert.Equal(value, ParameterTable.Get(changed, name));
  }

  [Theory]
  [InlineData("Bogus", 1d)]
  [InlineData("density", 1d)]
  // Computed from the others, never set.
  [InlineData("Packing", 1d)]
  [InlineData("Density", double.NaN)]
  [InlineData("Density", double.PositiveInfinity)]
  // No number, which a flag would otherwise read as set.
  [InlineData("IsLongAxisAcross", double.NaN)]
  [InlineData("Density", 0d)]
  [InlineData("Embed", 1.01d)]
  [InlineData("Layers", 3d)]
  [InlineData("Seed", -1d)]
  [InlineData("Seed", 4_294_967_296d)]
  public void With_ANameOrAValueTheParametersRefuse_GivesNone(string name, double value) {
    Assert.Null(ParameterTable.With(BreakwaterParameters.Default, name, value));
  }

  [Fact]
  public void Read_WhatWasWritten_IsEqual() {
    BreakwaterParameters[] all = [
      BreakwaterParameters.Default,
      ParameterTableTests.Unusual,
      .. BreakwaterStyles.All.Select(static style => style.Parameters)
    ];

    Assert.All(
      all,
      static parameters =>
        Assert.Equal(parameters, ParameterTable.Read(ParameterTable.Write(parameters)))
    );
  }

  [Fact]
  public void Write_NamesEachParameterOnce() {
    var names = ParameterTable.Write(BreakwaterParameters.Default)
      .Split(';')
      .Select(static pair => pair.Split('=')[0]);

    Assert.Equal(ParameterTable.Names, names);
  }

  [Theory]
  [InlineData("")]
  [InlineData("nothing of the kind")]
  [InlineData("Bogus=1;Embed=x;Layers=7;Density=;=2;Tumble=0.5=1")]
  public void Read_ATextWithNoParameter_GivesTheDefaults(string text) {
    Assert.Equal(BreakwaterParameters.Default, ParameterTable.Read(text));
  }

  [Fact]
  public void Read_ATextLackingNamesAndHoldingOthers_KeepsWhatItCan() {
    // A text an older or a newer version wrote, or one edited by hand.
    var parameters = ParameterTable.Read("Density=2; Bogus=1;Layers=7;Tumble=0.25;Embed=2");

    Assert.Equal(
      BreakwaterParameters.Default with {
        Density = 2f,
        Tumble = 0.25f
      },
      parameters
    );
  }
}
