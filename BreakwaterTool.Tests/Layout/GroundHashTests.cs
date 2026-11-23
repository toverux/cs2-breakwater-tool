using System.Text;
using BreakwaterTool.Layout;
using Unity.Collections;
using Unity.Mathematics;
using Xunit;

namespace BreakwaterTool.Tests.Layout;

public sealed class GroundHashTests {
  private const int Width = 8;

  private const int Height = 6;

  /// <summary>
  /// The cells from (2, 1) to (5, 4), both included.
  /// </summary>
  private static readonly (int2 Min, int2 Max) Cells = (new int2(2, 1), new int2(5, 4));

  [Theory]
  // FNV-1a's published 64-bit vectors, a cell holding two of the text's bytes, the low one first.
  [InlineData("", 0xcbf29ce484222325UL)]
  [InlineData("ab", 0x089c4407b545986aUL)]
  [InlineData("foobar", 0x85944171f73967e8UL)]
  public void Add_CellsOfAText_GivesItsFnv1aHash(string text, ulong expected) {
    var bytes = Encoding.ASCII.GetBytes(text);
    var grid = new ushort[math.max(1, bytes.Length / 2)];

    for (var i = 0; i < bytes.Length / 2; i++) {
      grid[i] = (ushort) (bytes[2 * i] | (bytes[(2 * i) + 1] << 8));
    }

    var hash = GroundHashTests.Hash(
      grid,
      (int2.zero, new int2((bytes.Length / 2) - 1, 0)),
      width: grid.Length
    );

    Assert.Equal(expected, hash);
  }

  [Fact]
  public void Add_TheSameCells_GivesTheSameHash() {
    var first = GroundHashTests.Grid();
    var second = GroundHashTests.Grid();

    Assert.Equal(GroundHashTests.Hash(first), GroundHashTests.Hash(second));
  }

  [Fact]
  public void Add_AnyOneCellChanged_GivesAnotherHash() {
    var grid = GroundHashTests.Grid();

    var hash = GroundHashTests.Hash(grid);

    for (var z = GroundHashTests.Cells.Min.y; z <= GroundHashTests.Cells.Max.y; z++) {
      for (var x = GroundHashTests.Cells.Min.x; x <= GroundHashTests.Cells.Max.x; x++) {
        var cell = (z * GroundHashTests.Width) + x;
        var height = grid[cell];

        // The smallest step of a height, then its high byte alone.
        grid[cell] = (ushort) (height + 1);
        Assert.NotEqual(hash, GroundHashTests.Hash(grid));

        grid[cell] = (ushort) (height ^ 0x0100);
        Assert.NotEqual(hash, GroundHashTests.Hash(grid));

        grid[cell] = height;
      }
    }

    Assert.Equal(hash, GroundHashTests.Hash(grid));
  }

  [Fact]
  public void Add_ACellOutsideTheCellsChanged_GivesTheSameHash() {
    var grid = GroundHashTests.Grid();

    var hash = GroundHashTests.Hash(grid);

    for (var z = 0; z < GroundHashTests.Height; z++) {
      for (var x = 0; x < GroundHashTests.Width; x++) {
        if (math.all(new int2(x, z) >= GroundHashTests.Cells.Min) &&
          math.all(new int2(x, z) <= GroundHashTests.Cells.Max)) {
          continue;
        }

        grid[(z * GroundHashTests.Width) + x]++;
      }
    }

    Assert.Equal(hash, GroundHashTests.Hash(grid));
  }

  [Fact]
  public void Add_TwoCellsSwapped_GivesAnotherHash() {
    var grid = GroundHashTests.Grid();

    var hash = GroundHashTests.Hash(grid);
    var first = (GroundHashTests.Cells.Min.y * GroundHashTests.Width) + GroundHashTests.Cells.Min.x;

    (grid[first], grid[first + 1]) = (grid[first + 1], grid[first]);

    Assert.NotEqual(hash, GroundHashTests.Hash(grid));
  }

  [Fact]
  public void Add_NoCell_LeavesTheHash() {
    var grid = GroundHashTests.Grid();

    // What an outline off the grid gives: its first cell past its last.
    var hash = GroundHashTests.Hash(grid, (new int2(3, 2), new int2(2, 1)), start: 42UL);

    Assert.Equal(42UL, hash);
  }

  [Fact]
  public void Add_ASecondGrid_ChainsOnTheFirst() {
    var grid = GroundHashTests.Grid();
    var backdrop = GroundHashTests.Grid();

    var alone = GroundHashTests.Hash(grid);
    var both = GroundHashTests.Hash(backdrop, start: alone);

    backdrop[(2 * GroundHashTests.Width) + 3]++;

    Assert.NotEqual(alone, both);
    Assert.NotEqual(both, GroundHashTests.Hash(backdrop, start: alone));
  }

  [Fact]
  public void Cells_AnOutline_CoversItsBoundsCellsBothEndsIncluded() {
    // Cells of 4 m whose first is at -8 m: the bounds run from cell 2.25 to 4.5, and 1.5 to 3.
    var cells = GroundHash.Cells(
      [new float2(1f, -2f), new float2(10f, 0f), new float2(4f, 4f)],
      static point => (point + 8f) / 4f,
      new int2(GroundHashTests.Width, GroundHashTests.Height)
    );

    Assert.Equal((new int2(2, 1), new int2(5, 3)), cells);
  }

  [Fact]
  public void Cells_AnOutlinePastTheGrid_StopsAtItsEdges() {
    var cells = GroundHash.Cells(
      [new float2(-100f, -100f), new float2(100f, 0f), new float2(0f, 100f)],
      static point => (point + 8f) / 4f,
      new int2(GroundHashTests.Width, GroundHashTests.Height)
    );

    Assert.Equal(
      (int2.zero, new int2(GroundHashTests.Width - 1, GroundHashTests.Height - 1)),
      cells
    );
  }

  [Fact]
  public void Cells_AnOutlineOffTheGrid_HoldsNoCell() {
    var grid = GroundHashTests.Grid();

    var cells = GroundHash.Cells(
      [new float2(100f, 100f), new float2(120f, 100f), new float2(110f, 120f)],
      static point => (point + 8f) / 4f,
      new int2(GroundHashTests.Width, GroundHashTests.Height)
    );

    Assert.Equal(GroundHash.Start, GroundHashTests.Hash(grid, cells));
  }

  [Fact]
  public void Cells_AGridTurnedOver_KeepsItsCellsInOrder() {
    // A grid whose cells run against the terrain's axes, as nothing forbids.
    var cells = GroundHash.Cells(
      [new float2(1f, -2f), new float2(10f, 0f), new float2(4f, 4f)],
      static point => (20f - point) / 4f,
      new int2(GroundHashTests.Width, GroundHashTests.Height)
    );

    Assert.Equal((new int2(2, 4), new int2(5, 5)), cells);
  }

  /// <summary>
  /// The hash of a grid's cells, the fixture's own unless given, into a running hash.
  /// </summary>
  private static ulong Hash(
    ushort[] grid,
    (int2 Min, int2 Max)? cells = null,
    ulong? start = null,
    int width = GroundHashTests.Width
  ) {
    using var native = new NativeArray<ushort>(grid, Allocator.Temp);

    return GroundHash.Add(
      start ?? GroundHash.Start,
      native,
      width,
      cells ?? GroundHashTests.Cells
    );
  }

  /// <summary>
  /// A grid whose every cell differs, in both its bytes.
  /// </summary>
  private static ushort[] Grid() {
    var grid = new ushort[GroundHashTests.Width * GroundHashTests.Height];

    for (var i = 0; i < grid.Length; i++) {
      grid[i] = (ushort) (1000 + (i * 263));
    }

    return grid;
  }
}
