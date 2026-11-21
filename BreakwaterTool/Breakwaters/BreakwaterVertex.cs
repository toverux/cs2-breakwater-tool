using Colossal.Serialization.Entities;
using Unity.Entities;
using Unity.Mathematics;

namespace BreakwaterTool.Breakwaters;

/// <summary>
/// A point of a breakwater's outline, in game metres seen from above, in drawing order.
/// </summary>
[InternalBufferCapacity(0)]
internal struct BreakwaterVertex(float2 position) : IBufferElementData, ISerializable {
  private const int FormatVersion = 1;

  public float2 position = position;

  public readonly void Serialize<TWriter>(TWriter writer) where TWriter : IWriter {
    writer.Write(BreakwaterVertex.FormatVersion);
    writer.Write(this.position);
  }

  public void Deserialize<TReader>(TReader reader) where TReader : IReader {
    reader.Read(out int _);
    reader.Read(out this.position);
  }
}
