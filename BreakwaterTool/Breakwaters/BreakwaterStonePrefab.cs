using Colossal.Serialization.Entities;
using Unity.Entities;

namespace BreakwaterTool.Breakwaters;

/// <summary>
/// A prefab of a breakwater's stone choice: a stone, or a random prefab whose variants it lays.
/// <see cref="BreakwaterPrefabReferencesSystem"/> keeps it valid across a save.
/// </summary>
[InternalBufferCapacity(0)]
internal struct BreakwaterStonePrefab(Entity prefab) : IBufferElementData, ISerializable {
  private const int FormatVersion = 1;

  public Entity prefab = prefab;

  public readonly void Serialize<TWriter>(TWriter writer) where TWriter : IWriter {
    writer.Write(BreakwaterStonePrefab.FormatVersion);
    writer.Write(this.prefab);
  }

  public void Deserialize<TReader>(TReader reader) where TReader : IReader {
    reader.Read(out int _);
    reader.Read(out this.prefab);
  }
}
