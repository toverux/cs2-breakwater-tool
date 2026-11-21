using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Xunit;

namespace BreakwaterTool.Tests;

/// <summary>
/// Pins the mod's public surface, what other mods compile against, in <c>PublicSurface.txt</c>: it
/// changes only on purpose, and a change that removes or alters a member is a major version.
/// </summary>
public sealed class PublicSurfaceTests {
  private const BindingFlags Declared =
    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

  private readonly NullabilityInfoContext nullability = new();

  [Fact]
  public void PublicSurface_IsTheApprovedOne() {
    var surface = new StringBuilder();

    foreach (var type in typeof(Mod).Assembly.GetExportedTypes().OrderBy(static t => t.FullName)) {
      surface.Append(PublicSurfaceTests.Kind(type)).Append(' ').AppendLine(type.FullName);

      var members = type.GetMembers(PublicSurfaceTests.Declared)
        .Where(static member => member is not MethodInfo { IsSpecialName: true })
        .Select(this.Describe)
        .Order(StringComparer.Ordinal);

      foreach (var member in members) {
        surface.Append("  ").AppendLine(member);
      }
    }

    Approval.Verify("PublicSurface.txt", surface.ToString());
  }

  private static string Kind(Type type) =>
    type switch {
      { IsEnum: true } => "enum",
      { IsValueType: true } => "struct",
      { IsInterface: true } => "interface",
      { IsAbstract: true, IsSealed: true } => "static class",
      _ when type.GetMethod("<Clone>$") is not null => "record",
      _ => "class"
    };

  private string Describe(MemberInfo member) =>
    member switch {
      ConstructorInfo constructor =>
        $"{PublicSurfaceTests.Static(constructor)}new({this.Parameters(constructor)})",
      MethodInfo method =>
        $"{PublicSurfaceTests.Static(method)}{this.Name(this.nullability.Create(method.ReturnParameter), method.ReturnType)} " +
        $"{method.Name}({this.Parameters(method)})",
      PropertyInfo property =>
        $"{(property.GetMethod?.IsStatic ?? false ? "static " : "")}{this.Name(this.nullability.Create(property), property.PropertyType)} " +
        $"{property.Name} {{ {(property.GetMethod?.IsPublic ?? false ? "get; " : "")}{PublicSurfaceTests.Setter(property)}}}",
      FieldInfo field =>
        $"{(field.IsStatic ? "static " : "")}{this.Name(this.nullability.Create(field), field.FieldType)} {field.Name}",
      Type nested => $"nested {nested.Name}",
      _ => $"{member.MemberType} {member.Name}"
    };

  private string Parameters(MethodBase method) =>
    string.Join(
      ", ",
      method.GetParameters()
        .Select(parameter =>
          $"{this.Name(this.nullability.Create(parameter), parameter.ParameterType)} {parameter.Name}" +
          (parameter.HasDefaultValue ? $" = {parameter.DefaultValue ?? "null"}" : "")
        )
    );

  private static string Static(MethodBase method) => method.IsStatic ? "static " : "";

  private static string Setter(PropertyInfo property) {
    if (property.SetMethod is not { IsPublic: true } setter) {
      return "";
    }

    var isInit = setter.ReturnParameter.GetRequiredCustomModifiers()
      .Any(static modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit"
      );

    return isInit ? "init; " : "set; ";
  }

  /// <summary>
  /// A type as C# writes it, with its generic arguments and the nullability of references.
  /// </summary>
  private string Name(NullabilityInfo info, Type type) {
    if (type.IsByRef) {
      return $"{this.Name(info, type.GetElementType()!)}&";
    }

    var name = type.IsGenericType
      ? $"{type.Namespace}.{type.Name[..type.Name.IndexOf('`')]}<" +
      string.Join(
        ", ",
        type.GetGenericArguments()
          .Select((argument, i) =>
            this.Name(info.GenericTypeArguments.ElementAtOrDefault(i) ?? info, argument)
          )
      ) +
      ">"
      : type.IsArray
        ? $"{this.Name(info.ElementType ?? info, type.GetElementType()!)}[]"
        : type.FullName ?? type.Name;

    return !type.IsValueType && info.ReadState is NullabilityState.Nullable ? $"{name}?" : name;
  }
}
