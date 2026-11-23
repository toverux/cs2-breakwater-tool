using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace BreakwaterTool;

/// <summary>
/// The layout parameters by name, each as a number: what the panel's controls set one at a time,
/// and the text the mod's settings keep the last ones used in.
/// A flag is 0 or 1, and a parameter of another name does not exist.
/// </summary>
internal static class ParameterTable {
  // [PERF] Resolved once, in place of a lookup by name at each read or write.
  private static readonly Dictionary<string, PropertyInfo> Properties =
    typeof(BreakwaterParameters)
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(static property => property.SetMethod is { IsPublic: true })
      .ToDictionary(static property => property.Name);

  /// <summary>
  /// The parameters' names, in declaration order.
  /// </summary>
  internal static IReadOnlyCollection<string> Names => ParameterTable.Properties.Keys;

  internal static double Get(BreakwaterParameters parameters, string name) =>
    Convert.ToDouble(
      ParameterTable.Properties[name].GetValue(parameters),
      CultureInfo.InvariantCulture
    );

  /// <summary>
  /// The parameters with one changed, or null when no parameter has that name or the value leaves
  /// them unable to lay a breakwater.
  /// </summary>
  internal static BreakwaterParameters? With(
    BreakwaterParameters parameters,
    string name,
    double value
  ) {
    if (!ParameterTable.Properties.TryGetValue(name, out var property) ||
      double.IsNaN(value) ||
      double.IsInfinity(value)) {
      return null;
    }

    object converted;

    try {
      converted = property.PropertyType == typeof(bool)
        ? value != 0d
        : Convert.ChangeType(value, property.PropertyType, CultureInfo.InvariantCulture);
    }
    catch (OverflowException) {
      // A number its integer type cannot hold, as a negative seed.
      return null;
    }

    var changed = parameters with {
    };

    // An init accessor is a setter to reflection, and the copy is not shared yet.
    property.SetValue(changed, converted);

    return changed.Validate() is null ? changed : null;
  }

  /// <summary>
  /// The parameters as text, <c>Name=value</c> pairs separated by semicolons.
  /// </summary>
  internal static string Write(BreakwaterParameters parameters) =>
    string.Join(
      ";",
      ParameterTable.Names.Select(name =>
        $"{name}={ParameterTable.Get(parameters, name).ToString("R", CultureInfo.InvariantCulture)}"
      )
    );

  /// <summary>
  /// The parameters a text holds, over the defaults: a name it lacks keeps its default, and a pair
  /// that is no parameter, no number or out of range is skipped.
  /// </summary>
  internal static BreakwaterParameters Read(string text) {
    var parameters = BreakwaterParameters.Default;

    foreach (var pair in text.Split([';'], StringSplitOptions.RemoveEmptyEntries)) {
      var parts = pair.Split('=');

      if (parts.Length == 2 &&
        double.TryParse(
          parts[1],
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out var value
        )) {
        parameters = ParameterTable.With(parameters, parts[0].Trim(), value) ?? parameters;
      }
    }

    return parameters;
  }
}
