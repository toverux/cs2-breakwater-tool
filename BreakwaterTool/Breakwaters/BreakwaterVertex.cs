using Colossal.Serialization.Entities;
using Unity.Entities;
using Unity.Mathematics;

namespace BreakwaterTool.Breakwaters;

/// <summary>
/// A point of a breakwater's outline, in game metres seen from above, in drawing order, and
/// whether the edge from it to the next point is toe, which counts only where the breakwater's
/// record says its toe is picked by hand.
/// </summary>
[InternalBufferCapacity(0)]
internal struct BreakwaterVertex(float2 position, bool isToe) : IBufferElementData, ISerializable {
  private const int FormatVersion = 1;

  public float2 position = position;

  public bool isToe = isToe;

  public readonly void Serialize<TWriter>(TWriter writer) where TWriter : IWriter {
    writer.Write(BreakwaterVertex.FormatVersion);
    writer.Write(this.position);
    writer.Write(this.isToe);
  }

  public void Deserialize<TReader>(TReader reader) where TReader : IReader {
    reader.Read(out int _);
    reader.Read(out this.position);
    reader.Read(out this.isToe);
  }
}
