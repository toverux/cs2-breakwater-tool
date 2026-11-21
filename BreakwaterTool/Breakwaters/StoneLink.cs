using Colossal.Serialization.Entities;
using Unity.Entities;

namespace BreakwaterTool.Breakwaters;

/// <summary>
/// Makes a stone one of a breakwater's: the breakwater's record entity.
/// A stone whose record is gone is a loose stone, which no operation touches.
/// </summary>
internal struct StoneLink(Entity record) : IComponentData, ISerializable {
  private const int FormatVersion = 1;

  public Entity record = record;

  public readonly void Serialize<TWriter>(TWriter writer) where TWriter : IWriter {
    writer.Write(StoneLink.FormatVersion);
    writer.Write(this.record);
  }

  public void Deserialize<TReader>(TReader reader) where TReader : IReader {
    reader.Read(out int _);
    reader.Read(out this.record);
  }
}
