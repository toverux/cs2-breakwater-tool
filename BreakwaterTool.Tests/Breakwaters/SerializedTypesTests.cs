using System;
using System.Linq;
using BreakwaterTool.Breakwaters;
using Colossal.Serialization.Entities;
using Unity.Entities;
using Unity.Mathematics;
using Xunit;

namespace BreakwaterTool.Tests.Breakwaters;

/// <summary>
/// The mod's types in a save: each writes its format version first, then its fields, and reads
/// back what it wrote. The types written, in order, are the format: a change to them changes what
/// a save holds.
/// </summary>
public sealed class SerializedTypesTests {
  [Fact]
  public void BreakwaterRecord_WrittenThenRead_IsEqual() {
    // Every field off its default, so that a field read into another shows.
    var parameters = new BreakwaterParameters {
      Density = 2.5f,
      Embed = 0.6f,
      Irregularity = 0.05f,
      Seed = 42,
      RowRatio = 0.8f,
      LevelTops = 0.3f,
      IsLongAxisAcross = true,
      FollowSlope = 0.7f,
      TurnJitter = 12f,
      SizeTolerance = 0.2f,
      Mixing = 0.4f,
      Layers = 2,
      UnderLayerDepth = 0.5f,
      Tumble = 0.1f,
      ToeShare = 0.3f,
      LevelHeight = 3f
    };

    var state = new BreakwaterState(
      [float2.zero],
      [],
      parameters,
      StoneProtectionKind.Anarchy,
      [true],
      0xFEDC_BA98_7654_3210UL
    );

    var (written, read) = SerializedTypesTests.RoundTrip(
      new BreakwaterRecord(state),
      typeof(int),
      typeof(float),
      typeof(float),
      typeof(float),
      typeof(uint),
      typeof(float),
      typeof(float),
      typeof(bool),
      typeof(float),
      typeof(float),
      typeof(float),
      typeof(float),
      typeof(int),
      typeof(float),
      typeof(float),
      typeof(float),
      typeof(float),
      typeof(byte),
      typeof(bool),
      typeof(ulong)
    );

    Assert.Equal(parameters, read.Parameters);
    Assert.Equal(StoneProtectionKind.Anarchy, read.protection);
    Assert.True(read.isToeByHand);
    Assert.Equal(0xFEDC_BA98_7654_3210UL, read.groundHash);
    Assert.Equal((byte) 1, written[^3]);
  }

  [Fact]
  public void BreakwaterVertex_WrittenThenRead_IsEqual() {
    var (_, read) = SerializedTypesTests.RoundTrip(
      new BreakwaterVertex(new float2(1901.2f, -2068.5f), true),
      typeof(int),
      typeof(float2),
      typeof(bool)
    );

    Assert.Equal(new float2(1901.2f, -2068.5f), read.position);
    Assert.True(read.isToe);
  }

  [Fact]
  public void BreakwaterStonePrefab_WrittenThenRead_IsEqual() {
    var prefab = new Entity {
      Index = 12,
      Version = 3
    };
    var (_, read) = SerializedTypesTests.RoundTrip(
      new BreakwaterStonePrefab(prefab),
      typeof(int),
      typeof(Entity)
    );

    Assert.Equal(prefab, read.prefab);
  }

  [Fact]
  public void StoneLink_WrittenThenRead_IsEqual() {
    var record = new Entity {
      Index = 7,
      Version = 1
    };
    var (_, read) = SerializedTypesTests.RoundTrip(
      new StoneLink(record),
      typeof(int),
      typeof(Entity)
    );

    Assert.Equal(record, read.record);
  }

  /// <summary>
  /// Writes a value, checks the types it wrote and its format version, the first value, and reads
  /// it back into a default one, which must consume every value written.
  /// </summary>
  private static (object[] Written, T Read) RoundTrip<T>(T value, params Type[] format)
    where T : struct, ISerializable {
    var (writer, written) = TapeWriter.Create();

    value.Serialize(writer);

    Assert.Equal(format, written.Values.Select(static v => v.GetType()));
    Assert.Equal(1, written.Values[0]);

    var (reader, left) = TapeReader.Create(written.Values);
    var read = default(T);

    read.Deserialize(reader);

    Assert.Equal(0, left.Left);

    return ([.. written.Values], read);
  }
}
