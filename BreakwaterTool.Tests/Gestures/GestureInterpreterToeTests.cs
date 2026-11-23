using BreakwaterTool.Gestures;
using Unity.Mathematics;
using Xunit;

namespace BreakwaterTool.Tests.Gestures;

/// <summary>
/// The toe edges picked by hand: a press on an edge's toe marker makes the edge toe or not,
/// starting from the toe the layout chose, and each flag follows its edge through the outline's
/// edits.
/// </summary>
public sealed class GestureInterpreterToeTests {
  private const int A = 1;

  private const int B = 2;

  /// <summary>
  /// Its edges, in order: z = 0, x = 10, z = 10 and x = 0.
  /// </summary>
  private static readonly float2[] SquareA = [
    new(0f, 0f), new(10f, 0f), new(10f, 10f), new(0f, 10f)
  ];

  private static readonly float2[] SquareB = [
    new(20f, 0f), new(30f, 0f), new(30f, 10f), new(20f, 10f)
  ];

  private readonly GestureInterpreter<int> gestures;

  // The toe the layout chooses for any outline of four edges.
  private bool[] chosen = [true, false, false, true];

  public GestureInterpreterToeTests() =>
    this.gestures = new GestureInterpreter<int>(
      [
        (GestureInterpreterToeTests.A, GestureInterpreterToeTests.SquareA),
        (GestureInterpreterToeTests.B, GestureInterpreterToeTests.SquareB)
      ],
      static _ => 0f,
      // B's toe is picked by hand, A's left to the layout.
      static breakwater => breakwater == GestureInterpreterToeTests.B
        ? [false, true, false, false]
        : null,
      _ => [.. this.chosen]
    );

  [Fact]
  public void Edit_WithToeEdges_HoldsThem() {
    this.EditA(true, false, false, false);

    Assert.Equal([true, false, false, false], this.gestures.Toe!);
  }

  [Theory]
  [InlineData(null)]
  [InlineData(new[] { true, false, false })]
  [InlineData(new bool[0])]
  public void Edit_WithNoToeEdgesOrNotOnePerEdge_LeavesTheToeToTheLayout(bool[]? toe) {
    this.gestures.Edit(GestureInterpreterToeTests.A, GestureInterpreterToeTests.SquareA, toe);

    Assert.Null(this.gestures.Toe);
  }

  [Fact]
  public void Reset_WhileTheToeIsByHand_DropsIt() {
    this.EditA(true, false, false, false);

    this.gestures.Reset();

    Assert.Null(this.gestures.Toe);
  }

  [Fact]
  public void Press_TheMarkerOfAnEdgeOffTheToe_MakesItToe() {
    this.EditA(true, false, false, false);

    var gesture = this.gestures.Press(GestureInterpreterToeTests.At(13f, 5f, marker: 1));

    Assert.Equal(GestureKind.ToeToggled, gesture.Kind);
    Assert.Equal(GestureInterpreterToeTests.A, gesture.Breakwater);
    Assert.Equal([true, true, false, false], gesture.Toe!);
    Assert.Equal(GestureInterpreterToeTests.SquareA, gesture.Polygon!);
    Assert.Equal(GestureInterpreterToeTests.SquareA, this.gestures.Points);
    Assert.Equal([true, true, false, false], this.gestures.Toe!);
    Assert.Equal(-1, this.gestures.Held);
  }

  [Fact]
  public void Press_TheMarkerOfAToeEdge_TakesItOffTheToe() {
    this.EditA(true, true, false, false);

    var gesture = this.gestures.Press(GestureInterpreterToeTests.At(5f, -3f, marker: 0));

    Assert.Equal(GestureKind.ToeToggled, gesture.Kind);
    Assert.Equal([false, true, false, false], gesture.Toe!);
  }

  [Fact]
  public void Press_TheMarkerOfTheLastToeEdge_RefusesIt() {
    this.EditA(true, false, false, false);

    var gesture = this.gestures.Press(GestureInterpreterToeTests.At(5f, -3f, marker: 0));

    Assert.Equal(new Gesture<int>(GestureKind.Refused, Refusal: GestureRefusal.KeepsToe), gesture);
    Assert.Equal([true, false, false, false], this.gestures.Toe!);
    Assert.Equal(GestureInterpreterToeTests.SquareA, this.gestures.Points);
  }

  [Fact]
  public void Press_AMarkerWhileTheLayoutChoosesTheToe_StartsFromItsChoice() {
    this.gestures.Edit(GestureInterpreterToeTests.A, GestureInterpreterToeTests.SquareA);

    var gesture = this.gestures.Press(GestureInterpreterToeTests.At(13f, 5f, marker: 1));

    Assert.Equal(GestureKind.ToeToggled, gesture.Kind);
    Assert.Equal([true, true, false, true], gesture.Toe!);
    Assert.Equal([true, true, false, true], this.gestures.Toe!);
  }

  [Fact]
  public void Press_TheMarkerOfTheOnlyEdgeTheLayoutChose_RefusesItAndLeavesTheToeToTheLayout() {
    this.chosen = [false, false, true, false];
    this.gestures.Edit(GestureInterpreterToeTests.A, GestureInterpreterToeTests.SquareA);

    var gesture = this.gestures.Press(GestureInterpreterToeTests.At(5f, 13f, marker: 2));

    Assert.Equal(new Gesture<int>(GestureKind.Refused, Refusal: GestureRefusal.KeepsToe), gesture);
    Assert.Null(this.gestures.Toe);
  }

  [Fact]
  public void Aim_AMarkerOverAnEdge_AimsTheMarker() {
    this.EditA(true, false, false, false);

    // On the edge z = 0, where the marker of another edge shows.
    var target = this.gestures.Aim(GestureInterpreterToeTests.At(5f, 0.3f, marker: 2));

    Assert.Equal(new GestureTarget<int>(TargetKind.ToeMarker, 2), target);
    Assert.Equal(GestureHint.ToggleToe, this.gestures.Hint(target));
  }

  [Fact]
  public void Aim_AMarkerOverAVertex_AimsTheVertex() {
    this.EditA(true, false, false, false);

    var target = this.gestures.Aim(GestureInterpreterToeTests.At(10f, 0f, marker: 2));

    Assert.Equal(TargetKind.Vertex, target.Kind);
  }

  [Fact]
  public void Aim_AMarkerWhileAVertexIsHeld_AimsNoMarker() {
    this.EditA(true, false, false, false);
    this.gestures.Press(GestureInterpreterToeTests.At(10f, 0f));

    var target = this.gestures.Aim(GestureInterpreterToeTests.At(13f, 5f, marker: 1));

    Assert.False(this.gestures.HasToeMarkers);
    Assert.NotEqual(TargetKind.ToeMarker, target.Kind);
  }

  [Fact]
  public void Aim_AMarkerWithNoBreakwaterPicked_AimsNoMarker() {
    var target = this.gestures.Aim(GestureInterpreterToeTests.At(13f, 5f, marker: 1));

    Assert.False(this.gestures.HasToeMarkers);
    Assert.Equal(TargetKind.Terrain, target.Kind);
  }

  [Fact]
  public void Secondary_OnAMarker_Unpicks() {
    this.EditA(true, false, false, false);

    var gesture = this.gestures.Secondary(GestureInterpreterToeTests.At(13f, 5f, marker: 1));

    Assert.Equal(new Gesture<int>(GestureKind.Unpicked), gesture);
  }

  [Theory]
  // A click on an edge lays a vertex there, whoever chooses the toe: both halves of the edge are
  // what it was.
  [InlineData(null, null)]
  [InlineData(new[] { true, false, false, true }, new[] { true, true, false, false, true })]
  public void Release_AfterAClickOnAnEdge_LaysTheVertex(bool[]? toe, bool[]? split) {
    this.gestures.Edit(GestureInterpreterToeTests.A, GestureInterpreterToeTests.SquareA, toe);

    var gesture = this.Click(5f, 0.3f);

    Assert.Equal(GestureKind.Reshaped, gesture.Kind);
    Assert.Equal(5, gesture.Polygon!.Length);
    Assert.Equal(split, gesture.Toe);
  }

  [Theory]
  // A drag on a toe edge, then on one off the toe: both halves are what the edge was.
  [InlineData(5f, 0.3f, new[] { true, true, false, true, false })]
  [InlineData(9.7f, 5f, new[] { true, false, false, true, false })]
  public void Release_AfterADragOnAnEdgeWhileTheToeIsByHand_SplitsItWithItsFlag(
    float x,
    float z,
    bool[] toe
  ) {
    this.EditA(true, false, true, false);
    this.gestures.Press(GestureInterpreterToeTests.At(x, z));
    this.gestures.Move(GestureInterpreterToeTests.At(x + 3f, z - 3f));

    var gesture = this.gestures.Release(GestureInterpreterToeTests.At(x + 3f, z - 3f));

    Assert.Equal(GestureKind.Reshaped, gesture.Kind);
    Assert.Equal(5, gesture.Polygon!.Length);
    Assert.Equal(toe, gesture.Toe!);
    Assert.Equal(toe, this.gestures.Toe!);
  }

  [Fact]
  public void Release_AfterDraggingAVertexWhileTheToeIsByHand_KeepsTheToe() {
    this.EditA(true, false, true, false);
    this.gestures.Press(GestureInterpreterToeTests.At(10f, 0f));
    this.gestures.Move(GestureInterpreterToeTests.At(12f, -2f));

    var gesture = this.gestures.Release(GestureInterpreterToeTests.At(12f, -2f));

    Assert.Equal(GestureKind.Reshaped, gesture.Kind);
    Assert.Equal([true, false, true, false], gesture.Toe!);
  }

  [Fact]
  public void Secondary_WhileHoldingAVertexInsertedOnAnEdge_PutsTheToeBack() {
    this.EditA(true, false, true, false);
    this.gestures.Press(GestureInterpreterToeTests.At(5f, 0.3f));
    this.gestures.Move(GestureInterpreterToeTests.At(5f, -3f));

    var gesture = this.gestures.Secondary(GestureInterpreterToeTests.At(5f, -3f));

    Assert.Equal(new Gesture<int>(GestureKind.Dropped), gesture);
    Assert.Equal(GestureInterpreterToeTests.SquareA, this.gestures.Points);
    Assert.Equal([true, false, true, false], this.gestures.Toe!);
  }

  [Fact]
  public void Secondary_AVertex_DropsTheFlagOfTheEdgeLeavingIt() {
    this.EditA(true, false, true, false);

    // The corner at (10, 0): the edge arriving at it, toe, now runs on to (10, 10).
    var gesture = this.gestures.Secondary(GestureInterpreterToeTests.At(10f, 0f));

    Assert.Equal(GestureKind.Reshaped, gesture.Kind);
    Assert.Equal([new float2(0f, 0f), new float2(10f, 10f), new float2(0f, 10f)], gesture.Polygon!);
    Assert.Equal([true, true, false], gesture.Toe!);
    Assert.Equal([true, true, false], this.gestures.Toe!);
  }

  [Fact]
  public void Secondary_TheVertexTheLastToeEdgeLeaves_HandsTheToeBackToTheLayout() {
    this.EditA(false, true, false, false);

    var gesture = this.gestures.Secondary(GestureInterpreterToeTests.At(10f, 0f));

    Assert.Equal(GestureKind.Reshaped, gesture.Kind);
    Assert.Null(gesture.Toe);
    Assert.Null(this.gestures.Toe);
  }

  [Fact]
  public void Press_AnotherBreakwatersEdge_TakesItsToeWithIt() {
    this.gestures.Press(GestureInterpreterToeTests.At(25f, 0.3f));

    Assert.Equal(GestureInterpreterToeTests.B, this.gestures.Picked);

    // B's four flags, the edge under the pointer split in two.
    Assert.Equal([false, false, true, false, false], this.gestures.Toe!);
  }

  [Fact]
  public void Secondary_AnotherBreakwatersVertex_TakesItsToeAndDropsTheEdgesFlag() {
    var gesture = this.gestures.Secondary(GestureInterpreterToeTests.At(20f, 10f));

    Assert.Equal(GestureKind.Reshaped, gesture.Kind);
    Assert.Equal(GestureInterpreterToeTests.B, gesture.Breakwater);
    Assert.Equal([false, true, false], gesture.Toe!);
  }

  /// <summary>
  /// The pointer on the ground at a point, seen from straight above, its ray on a toe marker or
  /// on none.
  /// </summary>
  private static Pointer<int> At(float x, float z, int marker = -1) =>
    new(new float2(x, z), 1f, null, new float3(x, 100f, z), marker);

  private void EditA(params bool[] toe) =>
    this.gestures.Edit(GestureInterpreterToeTests.A, GestureInterpreterToeTests.SquareA, toe);

  /// <summary>
  /// A press and a release on the same point.
  /// </summary>
  private Gesture<int> Click(float x, float z) {
    this.gestures.Press(GestureInterpreterToeTests.At(x, z));

    return this.gestures.Release(GestureInterpreterToeTests.At(x, z));
  }
}
