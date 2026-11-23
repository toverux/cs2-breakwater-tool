using System.Collections.Generic;
using BreakwaterTool.Breakwaters;
using Colossal.UI.Binding;

namespace BreakwaterTool.Utils.Writers;

/// <summary>
/// What the tool's panel shows of the look.
/// </summary>
/// <param name="Stones">How each chosen prefab fares, in the order chosen.</param>
/// <param name="Thumbnails">
/// Each chosen prefab's thumbnail URL, as <paramref name="Stones"/>.
/// </param>
/// <param name="Fallbacks">
/// Each chosen prefab's image where the game draws no thumbnail of it, as
/// <paramref name="Stones"/>.
/// </param>
/// <param name="Style">The built-in style the look is, or empty.</param>
internal sealed record LookView(
  BreakwaterParameters Parameters,
  IReadOnlyList<StoneVerdict> Stones,
  IReadOnlyList<string> Thumbnails,
  IReadOnlyList<string> Fallbacks,
  string Style
);

/// <summary>
/// The look's wire format: every parameter as a number under its own name, a flag as 0 or 1, the
/// stones and the style. Each type holds the same properties at every write.
/// </summary>
internal sealed class LookWriter : IWriter<LookView> {
  public void Write(IJsonWriter writer, LookView look) {
    writer.TypeBegin("BreakwaterTool.Look");

    writer.PropertyName("parameters");
    writer.TypeBegin("BreakwaterTool.Parameters");

    foreach (var name in ParameterTable.Names) {
      writer.PropertyName(name);
      writer.Write(ParameterTable.Get(look.Parameters, name));
    }

    writer.TypeEnd();

    writer.PropertyName("stones");
    writer.ArrayBegin((uint) look.Stones.Count);

    for (var i = 0; i < look.Stones.Count; i++) {
      var stone = look.Stones[i];

      writer.TypeBegin("BreakwaterTool.Stone");
      writer.PropertyName("prefab");
      writer.Write(stone.Prefab);
      writer.PropertyName("name");
      writer.Write(stone.Name);
      writer.PropertyName("thumbnail");
      writer.Write(look.Thumbnails[i]);
      writer.PropertyName("fallback");
      writer.Write(look.Fallbacks[i]);
      writer.PropertyName("stones");
      writer.Write(stone.Stones);
      writer.PropertyName("laid");
      writer.Write(stone.Laid);
      writer.PropertyName("isUnprotectable");
      writer.Write(stone.IsUnprotectable);
      writer.TypeEnd();
    }

    writer.ArrayEnd();

    writer.PropertyName("style");
    writer.Write(look.Style);

    writer.TypeEnd();
  }
}
