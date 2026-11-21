using System;
using System.Collections.Generic;
using BreakwaterTool.Gestures;
using Unity.Mathematics;
using Xunit;

namespace BreakwaterTool.Tests.Gestures;

public sealed class GestureInterpreterTests {
  private const int A = 1;

  private const int B = 2;

  private static readonly float2[] SquareA = [
    new(0f, 0f), new(10f, 0f), new(10f, 10f), new(0f, 10f)
  ];

  private static readonly float2[] SquareB = [
    new(20f, 0f), new(30f, 0f), new(30f, 10f), new(20f, 10f)
  ];

  private readonly List<(int Breakwater, float2[] Outline)> breakwaters = [
    (GestureInterpreterTests.A, GestureInterpreterTests.SquareA),
    (GestureInterpreterTests.B, GestureInterpreterTests.SquareB)
  ];

  private readonly GestureInterpreter<int> gestures;

  // The terrain's height at a point: level unless a test shapes it.
  private Func<float2, float> ground = static _ => 0f;

  public GestureInterpreterTests() {
    this.gestures = new GestureInterpreter<int>(this.breakwaters, at => this.ground(at));
  }

  [Fact]
  public void Press_OpenGround_StartsAnOutlineOnThePoint() {
    var gesture = this.gestures.Press(GestureInterpreterTests.At(50f, 50f));

    Assert.Equal(new Gesture<int>(GestureKind.PointAdded), gesture);
    Assert.Equal(GestureMode.Drawing, this.gestures.Mode);
    Assert.Equal([new float2(50f, 50f)], this.gestures.Points);
  }

  [Fact]
  public void Press_WithinReachOfAPointDrawn_RefusesIt() {
    this.Draw((50f, 50f), (60f, 50f));

    var gesture = this.gestures.Press(GestureInterpreterTests.At(60.5f, 50f));

    Assert.Equal(
      new Gesture<int>(GestureKind.Refused, Refusal: GestureRefusal.TooClose),
      gesture
    );
    Assert.Equal(2, this.gestures.Points.Count);
  }

  [Fact]
  public void Press_TheFirstPointOfThree_ClosesTheOutline() {
    this.Draw((50f, 50f), (60f, 50f), (60f, 60f));

    var gesture = this.gestures.Press(GestureInterpreterTests.At(50.5f, 50f));

    Assert.Equal(GestureKind.Closed, gesture.Kind);
    Assert.Equal(
      [new float2(50f, 50f), new float2(60f, 50f), new float2(60f, 60f)],
      gesture.Polygon!
    );
  }

  [Fact]
  public void Press_TheFirstPointOfTwo_RefusesIt() {
    this.Draw((50f, 50f), (60f, 50f));

    var gesture = this.gestures.Press(GestureInterpreterTests.At(50.5f, 50f));

    Assert.Equal(
      new Gesture<int>(GestureKind.Refused, Refusal: GestureRefusal.TooClose),
      gesture
    );
  }

  [Fact]
  public void Press_WhereThePointerMeetsNoGround_DoesNothing() {
    var gesture = this.gestures.Press(new Pointer<int>(null, 1f, null, float3.zero));

    Assert.Equal(new Gesture<int>(GestureKind.None), gesture);
    Assert.Equal(GestureMode.Idle, this.gestures.Mode);
  }

  [Fact]
  public void Press_OpenGroundWhileOneIsPicked_LeavesThePickAndStartsAnOutline() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);

    var gesture = this.gestures.Press(GestureInterpreterTests.At(50f, 50f));

    Assert.Equal(new Gesture<int>(GestureKind.PointAdded), gesture);
    Assert.Equal(GestureMode.Drawing, this.gestures.Mode);
    Assert.Null(this.gestures.Picked);
    Assert.Equal([new float2(50f, 50f)], this.gestures.Points);
  }

  [Fact]
  public void Secondary_WhileDrawing_RemovesTheLastPoint() {
    this.Draw((50f, 50f), (60f, 50f));

    var gesture = this.gestures.Secondary(GestureInterpreterTests.At(70f, 50f));

    Assert.Equal(new Gesture<int>(GestureKind.PointRemoved), gesture);
    Assert.Equal(GestureMode.Drawing, this.gestures.Mode);
    Assert.Equal([new float2(50f, 50f)], this.gestures.Points);
  }

  [Fact]
  public void Secondary_OnTheOnlyPointDrawn_StopsDrawing() {
    this.Draw((50f, 50f));

    var gesture = this.gestures.Secondary(GestureInterpreterTests.At(50f, 50f));

    Assert.Equal(new Gesture<int>(GestureKind.PointRemoved), gesture);
    Assert.Equal(GestureMode.Idle, this.gestures.Mode);
    Assert.Empty(this.gestures.Points);
  }

  [Fact]
  public void Reset_WhileDrawing_DropsTheOutline() {
    this.Draw((50f, 50f), (60f, 50f));

    this.gestures.Reset();

    Assert.Equal(GestureMode.Idle, this.gestures.Mode);
    Assert.Empty(this.gestures.Points);
  }

  [Fact]
  public void Reset_WhileEditing_DropsThePick() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);

    this.gestures.Reset();

    Assert.Equal(GestureMode.Idle, this.gestures.Mode);
    Assert.Null(this.gestures.Picked);
    Assert.Empty(this.gestures.Points);
  }

  [Fact]
  public void Press_InsideAnotherBreakwater_PicksIt() {
    var gesture = this.gestures.Press(
      GestureInterpreterTests.Inside(25f, 5f, GestureInterpreterTests.B)
    );

    Assert.Equal(
      new Gesture<int>(GestureKind.Picked, Breakwater: GestureInterpreterTests.B),
      gesture
    );
  }

  [Fact]
  public void Press_InsideThePickedBreakwater_DoesNothing() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);

    var gesture = this.gestures.Press(
      GestureInterpreterTests.Inside(5f, 5f, GestureInterpreterTests.A)
    );

    Assert.Equal(new Gesture<int>(GestureKind.None), gesture);
    Assert.Equal(GestureInterpreterTests.A, this.gestures.Picked);
  }

  [Fact]
  public void Press_NearAVertexAndNearerItsEdge_HoldsTheVertex() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);

    var gesture = this.gestures.Press(GestureInterpreterTests.At(9.5f, 0.1f));

    Assert.Equal(new Gesture<int>(GestureKind.Grabbed), gesture);
    Assert.Equal(1, this.gestures.Held);
    Assert.Equal(GestureInterpreterTests.SquareA, this.gestures.Points);
  }

  [Fact]
  public void Press_WhileHoldingAVertex_DoesNothing() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);
    this.gestures.Press(GestureInterpreterTests.At(10f, 0f));

    var gesture = this.gestures.Press(GestureInterpreterTests.At(0f, 0f));

    Assert.Equal(new Gesture<int>(GestureKind.None), gesture);
    Assert.Equal(1, this.gestures.Held);
  }

  [Fact]
  public void Move_WithNothingHeld_DoesNothing() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);

    var gesture = this.gestures.Move(GestureInterpreterTests.At(10f, 0f));

    Assert.Equal(new Gesture<int>(GestureKind.None), gesture);
    Assert.Equal(GestureInterpreterTests.SquareA, this.gestures.Points);
  }

  [Fact]
  public void Move_WithinTheDeadZone_LeavesTheVertexInPlace() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);
    this.gestures.Press(GestureInterpreterTests.At(10.25f, 0f));

    var gesture = this.gestures.Move(GestureInterpreterTests.At(10.5f, 0.25f));

    Assert.Equal(new Gesture<int>(GestureKind.None), gesture);
    Assert.Equal(new float2(10f, 0f), this.gestures.Points[1]);
  }

  [Fact]
  public void Move_PastTheDeadZone_KeepsTheVertexAtItsGrabOffset() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);
    this.gestures.Press(GestureInterpreterTests.At(10.25f, 0.5f));

    var gesture = this.gestures.Move(GestureInterpreterTests.At(13f, 4f));

    Assert.Equal(new Gesture<int>(GestureKind.Moved), gesture);
    Assert.Equal(new float2(12.75f, 3.5f), this.gestures.Points[1]);
  }

  [Fact]
  public void Release_AfterDraggingAVertexOfThePicked_LaysItReshaped() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);
    this.gestures.Press(GestureInterpreterTests.At(10f, 0f));
    this.gestures.Move(GestureInterpreterTests.At(12f, -2f));

    var gesture = this.gestures.Release(GestureInterpreterTests.At(12f, -2f));

    Assert.Equal(GestureKind.Reshaped, gesture.Kind);
    Assert.Equal(GestureInterpreterTests.A, gesture.Breakwater);
    Assert.Equal(
      [new float2(0f, 0f), new float2(12f, -2f), new float2(10f, 10f), new float2(0f, 10f)],
      gesture.Polygon!
    );
    Assert.Equal(-1, this.gestures.Held);
  }

  [Fact]
  public void Release_AfterInsertingWithoutADrag_LaysTheVertexOnTheEdge() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);
    this.gestures.Press(GestureInterpreterTests.At(5f, 0.3f));

    var gesture = this.gestures.Release(GestureInterpreterTests.At(5f, 0.3f));

    Assert.Equal(GestureKind.Reshaped, gesture.Kind);
    Assert.Equal(
      [
        new float2(0f, 0f),
        new float2(5f, 0f),
        new float2(10f, 0f),
        new float2(10f, 10f),
        new float2(0f, 10f)
      ],
      gesture.Polygon!
    );
  }

  [Fact]
  public void Secondary_WhileDraggingAVertex_PutsTheOutlineBack() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);
    this.gestures.Press(GestureInterpreterTests.At(10f, 0f));
    this.gestures.Move(GestureInterpreterTests.At(13f, 0f));

    var gesture = this.gestures.Secondary(GestureInterpreterTests.At(13f, 0f));

    Assert.Equal(new Gesture<int>(GestureKind.Dropped), gesture);
    Assert.Equal(GestureInterpreterTests.SquareA, this.gestures.Points);
    Assert.Equal(-1, this.gestures.Held);
    Assert.Equal(GestureInterpreterTests.A, this.gestures.Picked);
  }

  [Fact]
  public void Secondary_WhileHoldingAnInsertedVertex_TakesItBackOut() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);
    this.gestures.Press(GestureInterpreterTests.At(5f, 0.3f));

    var gesture = this.gestures.Secondary(GestureInterpreterTests.At(5f, 0.3f));

    Assert.Equal(new Gesture<int>(GestureKind.Dropped), gesture);
    Assert.Equal(GestureInterpreterTests.SquareA, this.gestures.Points);
  }

  [Fact]
  public void Secondary_AVertexOfThePicked_RemovesIt() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);

    var gesture = this.gestures.Secondary(GestureInterpreterTests.At(10f, 10f));

    Assert.Equal(GestureKind.Reshaped, gesture.Kind);
    Assert.Equal(GestureInterpreterTests.A, gesture.Breakwater);
    Assert.Equal([new float2(0f, 0f), new float2(10f, 0f), new float2(0f, 10f)], gesture.Polygon!);
  }

  [Fact]
  public void Secondary_AVertexOfThePickedTriangle_RefusesIt() {
    this.gestures.Edit(
      GestureInterpreterTests.A,
      [new float2(0f, 0f), new float2(10f, 0f), new float2(0f, 10f)]
    );

    var gesture = this.gestures.Secondary(GestureInterpreterTests.At(10f, 0f));

    Assert.Equal(
      new Gesture<int>(GestureKind.Refused, Refusal: GestureRefusal.KeepsThree),
      gesture
    );
    Assert.Equal(3, this.gestures.Points.Count);
  }

  [Fact]
  public void Secondary_AwayFromTheHandlesWhileEditing_Unpicks() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);

    var gesture = this.gestures.Secondary(
      GestureInterpreterTests.Inside(5f, 5f, GestureInterpreterTests.A)
    );

    Assert.Equal(new Gesture<int>(GestureKind.Unpicked), gesture);
    Assert.Equal(GestureMode.Idle, this.gestures.Mode);
    Assert.Null(this.gestures.Picked);
  }

  [Fact]
  public void Hint_WhereThePointerMeetsNoGround_IsNone() {
    Assert.Equal(
      GestureHint.None,
      this.gestures.Hint(new Pointer<int>(null, 1f, null, float3.zero))
    );
  }

  [Fact]
  public void Hint_OpenGround_OffersToPlaceTheFirstPoint() {
    Assert.Equal(GestureHint.PlaceFirst, this.gestures.Hint(GestureInterpreterTests.At(50f, 50f)));
  }

  [Fact]
  public void Hint_WhileDrawingTwoPoints_OffersTheNextPoint() {
    this.Draw((50f, 50f), (60f, 50f));

    Assert.Equal(GestureHint.PlaceNext, this.gestures.Hint(GestureInterpreterTests.At(60f, 60f)));
  }

  [Fact]
  public void Hint_WhileDrawingThreePoints_OffersAPointOrTheClose() {
    this.Draw((50f, 50f), (60f, 50f), (60f, 60f));

    Assert.Equal(
      GestureHint.PlaceOrClose,
      this.gestures.Hint(GestureInterpreterTests.At(50f, 60f))
    );
  }

  [Fact]
  public void Hint_TheFirstPointOfThree_OffersTheClose() {
    this.Draw((50f, 50f), (60f, 50f), (60f, 60f));

    Assert.Equal(GestureHint.Close, this.gestures.Hint(GestureInterpreterTests.At(50f, 50f)));
  }

  [Fact]
  public void Hint_InsideAnotherBreakwater_OffersThePick() {
    Assert.Equal(
      GestureHint.Pick,
      this.gestures.Hint(GestureInterpreterTests.Inside(25f, 5f, GestureInterpreterTests.B))
    );
  }

  [Fact]
  public void Hint_AVertexOfATriangle_OffersTheMoveOnly() {
    this.gestures.Edit(
      GestureInterpreterTests.A,
      [new float2(0f, 0f), new float2(10f, 0f), new float2(0f, 10f)]
    );

    Assert.Equal(GestureHint.Move, this.gestures.Hint(GestureInterpreterTests.At(10f, 0f)));
  }

  [Fact]
  public void Hint_AnEdge_OffersTheInsert() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);

    Assert.Equal(GestureHint.Insert, this.gestures.Hint(GestureInterpreterTests.At(5f, 0.3f)));
  }

  [Fact]
  public void Hint_InsideThePickedBreakwater_OffersTheReshape() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);

    Assert.Equal(
      GestureHint.Reshape,
      this.gestures.Hint(GestureInterpreterTests.Inside(5f, 5f, GestureInterpreterTests.A))
    );
  }

  [Fact]
  public void Hint_OpenGroundWhileEditing_OffersAnotherOutline() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);

    Assert.Equal(
      GestureHint.StartAnother,
      this.gestures.Hint(GestureInterpreterTests.At(50f, 50f))
    );
  }

  [Fact]
  public void Hint_WhileHoldingAVertex_OffersTheRelease() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);
    this.gestures.Press(GestureInterpreterTests.At(10f, 0f));

    Assert.Equal(GestureHint.Release, this.gestures.Hint(GestureInterpreterTests.At(10f, 0f)));
  }

  [Fact]
  public void Press_AnotherBreakwatersVertex_PicksItAndHoldsTheVertex() {
    var gesture = this.gestures.Press(GestureInterpreterTests.At(20.2f, 0.1f));

    Assert.Equal(
      new Gesture<int>(GestureKind.Grabbed, Breakwater: GestureInterpreterTests.B),
      gesture
    );
    Assert.Equal(GestureInterpreterTests.B, this.gestures.Picked);
    Assert.Equal(0, this.gestures.Held);
    Assert.False(this.gestures.IsHeldOnEdge);
    Assert.False(this.gestures.IsHeldInserted);
    Assert.Equal(GestureInterpreterTests.SquareB, this.gestures.Points);
  }

  [Fact]
  public void Release_AfterDraggingAnotherBreakwatersVertex_LaysItReshaped() {
    this.gestures.Press(GestureInterpreterTests.At(20f, 0f));
    this.gestures.Move(GestureInterpreterTests.At(18f, -3f));

    var gesture = this.gestures.Release(GestureInterpreterTests.At(18f, -3f));

    Assert.Equal(GestureKind.Reshaped, gesture.Kind);
    Assert.Equal(GestureInterpreterTests.B, gesture.Breakwater);
    Assert.Equal(
      [new float2(18f, -3f), new float2(30f, 0f), new float2(30f, 10f), new float2(20f, 10f)],
      gesture.Polygon!
    );
  }

  [Fact]
  public void Release_WithinTheDeadZoneOfAnotherBreakwatersVertex_PicksItAndLaysNothing() {
    this.gestures.Press(GestureInterpreterTests.At(20f, 0f));

    var gesture = this.gestures.Release(GestureInterpreterTests.At(20.1f, 0f));

    Assert.Equal(GestureKind.Dropped, gesture.Kind);
    Assert.Equal(GestureInterpreterTests.B, this.gestures.Picked);
  }

  [Fact]
  public void Press_AnotherBreakwatersVertexWhileOneIsPicked_PicksTheOther() {
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);

    var gesture = this.gestures.Press(GestureInterpreterTests.At(30f, 10f));

    Assert.Equal(
      new Gesture<int>(GestureKind.Grabbed, Breakwater: GestureInterpreterTests.B),
      gesture
    );
    Assert.Equal(GestureInterpreterTests.B, this.gestures.Picked);
    Assert.Equal(2, this.gestures.Held);
  }

  [Fact]
  public void Press_VerticesOfThePickedAndAnotherWithinReach_HoldsThePickedOnes() {
    // The other breakwater's vertex lies nearer the pointer than the picked one's.
    this.breakwaters.Add((3, [new float2(10.6f, 0f), new float2(20f, 0f), new float2(15f, 5f)]));
    this.gestures.Edit(GestureInterpreterTests.A, GestureInterpreterTests.SquareA);

    var gesture = this.gestures.Press(GestureInterpreterTests.At(10.4f, 0f));

    Assert.Equal(new Gesture<int>(GestureKind.Grabbed), gesture);
    Assert.Equal(GestureInterpreterTests.A, this.gestures.Picked);
    Assert.Equal(1, this.gestures.Held);
  }

  [Fact]
  public void Press_AnotherBreakwatersEdge_PicksItAndInsertsAVertex() {
    var gesture = this.gestures.Press(GestureInterpreterTests.At(25f, 0.3f));

    Assert.Equal(
      new Gesture<int>(GestureKind.Inserted, Breakwater: GestureInterpreterTests.B),
      gesture
    );
    Assert.Equal(GestureInterpreterTests.B, this.gestures.Picked);
    Assert.Equal(1, this.gestures.Held);
    Assert.Equal(
      [
        new float2(20f, 0f),
        new float2(25f, 0f),
        new float2(30f, 0f),
        new float2(30f, 10f),
        new float2(20f, 10f)
      ],
      this.gestures.Points
    );
  }

  [Fact]
  public void Secondary_AnotherBreakwatersVertex_PicksItAndRemovesTheVertex() {
    var gesture = this.gestures.Secondary(GestureInterpreterTests.At(30f, 10f));

    Assert.Equal(GestureKind.Reshaped, gesture.Kind);
    Assert.Equal(GestureInterpreterTests.B, gesture.Breakwater);
    Assert.Equal(
      [new float2(20f, 0f), new float2(30f, 0f), new float2(20f, 10f)],
      gesture.Polygon!
    );
    Assert.Equal(GestureInterpreterTests.B, this.gestures.Picked);
  }

  [Fact]
  public void Secondary_AVertexOfAnotherTriangle_RefusesAndPicksNothing() {
    this.breakwaters.Add((3, [new float2(40f, 0f), new float2(50f, 0f), new float2(45f, 5f)]));

    var gesture = this.gestures.Secondary(GestureInterpreterTests.At(50f, 0f));

    Assert.Equal(
      new Gesture<int>(GestureKind.Refused, Refusal: GestureRefusal.KeepsThree),
      gesture
    );
    Assert.Null(this.gestures.Picked);
  }

  [Fact]
  public void Press_WhereTheRayPassesAnEdgeAboveTheGround_InsertsAVertexOnIt() {
    this.ShapeAValley();

    var gesture = this.gestures.Press(GestureInterpreterTests.PastTheValleysEdge());

    Assert.Equal(
      new Gesture<int>(GestureKind.Inserted, Breakwater: GestureInterpreterTests.A),
      gesture
    );
    Assert.Equal(
      [
        new float2(0f, 0f),
        new float2(5f, 0f),
        new float2(10f, 0f),
        new float2(10f, 10f),
        new float2(0f, 10f)
      ],
      this.gestures.Points
    );
  }

  [Fact]
  public void Move_AfterInsertingOnAnEdgeAboveTheGround_PutsTheVertexUnderThePointer() {
    this.ShapeAValley();
    this.gestures.Press(GestureInterpreterTests.PastTheValleysEdge());

    this.gestures.Move(GestureInterpreterTests.At(6f, 6f));

    Assert.Equal(new float2(6f, 6f), this.gestures.Points[1]);
  }

  [Fact]
  public void Press_AnEdge_HoldsTheVertexOnTheEdgeUntilTheDragStarts() {
    this.gestures.Press(GestureInterpreterTests.At(5f, 0.3f));

    Assert.True(this.gestures.IsHeldOnEdge);
    Assert.True(this.gestures.IsHeldInserted);

    this.gestures.Move(GestureInterpreterTests.At(5.2f, 0.3f));

    Assert.True(this.gestures.IsHeldOnEdge);
    Assert.Equal(new float2(5f, 0f), this.gestures.Points[1]);

    this.gestures.Move(GestureInterpreterTests.At(6f, 2f));

    Assert.False(this.gestures.IsHeldOnEdge);
    Assert.True(this.gestures.IsHeldInserted);
  }

  [Fact]
  public void Press_OnTheGroundUnderAnEdgeSeenAslant_PicksTheBreakwaterInstead() {
    this.ShapeAValley();

    // The ground at (0.3, 0, 5) lies 0.3 m from the edge at x = 0 seen from above, but the edge
    // runs 5 m over it, and the ray from the side passes it far below.
    var pointer = new Pointer<int>(
      new float2(0.3f, 5f),
      1f,
      GestureInterpreterTests.A,
      new float3(30f, 20f, 5f)
    );

    var gesture = this.gestures.Press(pointer);

    Assert.Equal(
      new Gesture<int>(GestureKind.Picked, Breakwater: GestureInterpreterTests.A),
      gesture
    );
  }

  [Fact]
  public void Secondary_WithNothingDrawnOrPicked_DoesNothing() {
    var gesture = this.gestures.Secondary(GestureInterpreterTests.At(50f, 50f));

    Assert.Equal(new Gesture<int>(GestureKind.None), gesture);
    Assert.Equal(GestureMode.Idle, this.gestures.Mode);
  }

  [Fact]
  public void Press_AnotherBreakwatersVertexWhileDrawing_PlacesAPoint() {
    this.gestures.Press(GestureInterpreterTests.At(50f, 50f));

    var gesture = this.gestures.Press(GestureInterpreterTests.At(20f, 0f));

    Assert.Equal(new Gesture<int>(GestureKind.PointAdded), gesture);
    Assert.Equal(GestureMode.Drawing, this.gestures.Mode);
    Assert.Null(this.gestures.Picked);
    Assert.Equal([new float2(50f, 50f), new float2(20f, 0f)], this.gestures.Points);
  }

  [Fact]
  public void Hint_AnotherBreakwatersVertex_OffersToMoveOrRemoveIt() {
    Assert.Equal(
      GestureHint.MoveOrRemove,
      this.gestures.Hint(GestureInterpreterTests.At(20f, 0f))
    );
  }

  /// <summary>
  /// The pointer on the ground at a point, seen from straight above.
  /// </summary>
  private static Pointer<int> At(float x, float z) =>
    new(new float2(x, z), 1f, null, new float3(x, 100f, z));

  /// <summary>
  /// The pointer on the ground inside a breakwater's outline, seen from straight above.
  /// </summary>
  private static Pointer<int> Inside(float x, float z, int breakwater) =>
    new(new float2(x, z), 1f, breakwater, new float3(x, 100f, z));

  /// <summary>
  /// Draws an outline on open ground, a press on each point.
  /// </summary>
  private void Draw(params (float X, float Z)[] points) {
    foreach (var (x, z) in points) {
      this.gestures.Press(GestureInterpreterTests.At(x, z));
    }
  }

  /// <summary>
  /// The pointer on the floor of <see cref="ShapeAValley"/> at (5, 1, 4), its ray passing square
  /// A's edge at z = 0 at (5, 5, 0), 4 m away from the pointer on the ground.
  /// </summary>
  private static Pointer<int> PastTheValleysEdge() =>
    new(new float2(5f, 4f), 1f, GestureInterpreterTests.A, new float3(5f, 13f, -8f));

  /// <summary>
  /// Shapes a valley across square A, 5 m deep along its middle: its four nodes stand 5 m up, its
  /// edges at x = 0 and x = 10 run 5 m above the valley floor, and its edges at z = 0 and z = 10
  /// lie on the ground.
  /// </summary>
  private void ShapeAValley() => this.ground = static at => math.abs(at.y - 5f);
}
