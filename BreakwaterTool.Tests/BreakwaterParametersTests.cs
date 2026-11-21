using System.Reflection;
using Xunit;

namespace BreakwaterTool.Tests;

public sealed class BreakwaterParametersTests {
  /// <summary>
  /// Every float parameter, a later one included, with each value no breakwater takes: not a finite
  /// number, or of a magnitude no layout can space stones by.
  /// </summary>
  public static TheoryData<string, float> OutOfRange { get; } = BreakwaterParametersTests.Floats();

  [Fact]
  public void Validate_Default_IsValid() => Assert.Null(BreakwaterParameters.Default.Validate());

  [Theory]
  [MemberData(nameof(BreakwaterParametersTests.OutOfRange))]
  [InlineData(nameof(BreakwaterParameters.Density), 1e-30f)]
  [InlineData(nameof(BreakwaterParameters.RowRatio), 1e-30f)]
  public void Validate_OutOfRangeFloat_SaysWhy(string property, float value) {
    var parameters = BreakwaterParameters.Default with {
    };

    typeof(BreakwaterParameters).GetProperty(property)!.SetValue(parameters, value);

    Assert.NotNull(parameters.Validate());
  }

  private static TheoryData<string, float> Floats() {
    var data = new TheoryData<string, float>();

    foreach (var property in typeof(BreakwaterParameters).GetProperties(
        BindingFlags.Public | BindingFlags.Instance
      )) {
      if (property.PropertyType != typeof(float) || !property.CanWrite) {
        continue;
      }

      float[] values = [float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1e30f, -1e30f];

      foreach (var value in values) {
        data.Add(property.Name, value);
      }
    }

    return data;
  }
}
