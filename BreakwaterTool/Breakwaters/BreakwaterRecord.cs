using Colossal.Serialization.Entities;
using Unity.Entities;

namespace BreakwaterTool.Breakwaters;

/// <summary>
/// A breakwater: its layout parameters, its protection, whether its toe is picked by hand and the
/// ground its stones were laid on, on an entity of its own that also holds
/// its outline (<see cref="BreakwaterVertex"/>) and its stone choice
/// (<see cref="BreakwaterStonePrefab"/>). Its stones point at it through their
/// <see cref="StoneLink"/>; it lists none of them.
/// </summary>
internal struct BreakwaterRecord : IComponentData, ISerializable {
  private const int FormatVersion = 1;

  public float density;

  public float embed;

  public float irregularity;

  public uint seed;

  public float rowRatio;

  public float levelTops;

  public bool isLongAxisAcross;

  public float followSlope;

  public float turnJitter;

  public float sizeTolerance;

  public float mixing;

  public int layers;

  public float underLayerDepth;

  public float tumble;

  public float toeShare;

  public float levelHeight;

  public StoneProtectionKind protection;

  /// <summary>
  /// Whether the toe is the edges flagged in the outline, in place of the layout's own choice.
  /// </summary>
  public bool isToeByHand;

  /// <summary>
  /// The hash of the ground the stones were laid on (<see cref="GroundHash"/>).
  /// </summary>
  public ulong groundHash;

  internal BreakwaterRecord(BreakwaterState state) {
    var parameters = state.Parameters;

    this.density = parameters.Density;
    this.embed = parameters.Embed;
    this.irregularity = parameters.Irregularity;
    this.seed = parameters.Seed;
    this.rowRatio = parameters.RowRatio;
    this.levelTops = parameters.LevelTops;
    this.isLongAxisAcross = parameters.IsLongAxisAcross;
    this.followSlope = parameters.FollowSlope;
    this.turnJitter = parameters.TurnJitter;
    this.sizeTolerance = parameters.SizeTolerance;
    this.mixing = parameters.Mixing;
    this.layers = parameters.Layers;
    this.underLayerDepth = parameters.UnderLayerDepth;
    this.tumble = parameters.Tumble;
    this.toeShare = parameters.ToeShare;
    this.levelHeight = parameters.LevelHeight;
    this.protection = state.Protection;
    this.isToeByHand = state.Toe is not null;
    this.groundHash = state.GroundHash;
  }

  internal readonly BreakwaterParameters Parameters =>
    new() {
      Density = this.density,
      Embed = this.embed,
      Irregularity = this.irregularity,
      Seed = this.seed,
      RowRatio = this.rowRatio,
      LevelTops = this.levelTops,
      IsLongAxisAcross = this.isLongAxisAcross,
      FollowSlope = this.followSlope,
      TurnJitter = this.turnJitter,
      SizeTolerance = this.sizeTolerance,
      Mixing = this.mixing,
      Layers = this.layers,
      UnderLayerDepth = this.underLayerDepth,
      Tumble = this.tumble,
      ToeShare = this.toeShare,
      LevelHeight = this.levelHeight
    };

  public readonly void Serialize<TWriter>(TWriter writer) where TWriter : IWriter {
    writer.Write(BreakwaterRecord.FormatVersion);
    writer.Write(this.density);
    writer.Write(this.embed);
    writer.Write(this.irregularity);
    writer.Write(this.seed);
    writer.Write(this.rowRatio);
    writer.Write(this.levelTops);
    writer.Write(this.isLongAxisAcross);
    writer.Write(this.followSlope);
    writer.Write(this.turnJitter);
    writer.Write(this.sizeTolerance);
    writer.Write(this.mixing);
    writer.Write(this.layers);
    writer.Write(this.underLayerDepth);
    writer.Write(this.tumble);
    writer.Write(this.toeShare);
    writer.Write(this.levelHeight);
    writer.Write((byte) this.protection);
    writer.Write(this.isToeByHand);
    writer.Write(this.groundHash);
  }

  public void Deserialize<TReader>(TReader reader) where TReader : IReader {
    // Version 1 is the only format yet: a later one branches on it here.
    reader.Read(out int _);
    reader.Read(out this.density);
    reader.Read(out this.embed);
    reader.Read(out this.irregularity);
    reader.Read(out this.seed);
    reader.Read(out this.rowRatio);
    reader.Read(out this.levelTops);
    reader.Read(out this.isLongAxisAcross);
    reader.Read(out this.followSlope);
    reader.Read(out this.turnJitter);
    reader.Read(out this.sizeTolerance);
    reader.Read(out this.mixing);
    reader.Read(out this.layers);
    reader.Read(out this.underLayerDepth);
    reader.Read(out this.tumble);
    reader.Read(out this.toeShare);
    reader.Read(out this.levelHeight);
    reader.Read(out byte protectionByte);
    reader.Read(out this.isToeByHand);
    reader.Read(out this.groundHash);

    this.protection = (StoneProtectionKind) protectionByte;
  }
}
