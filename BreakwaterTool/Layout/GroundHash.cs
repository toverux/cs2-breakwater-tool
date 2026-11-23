using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;

namespace BreakwaterTool.Layout;

/// <summary>
/// The hash of the ground under an outline: of the terrain's raw height cells within the outline's
/// bounds. The stones stay where they were laid when the terrain changes, so a breakwater whose
/// ground no longer gives the hash taken at its last lay is out of date, and Fit to terrain lays it
/// again.
/// The cells are the heightmap's own integers, so the same ground gives the same hash on every
/// machine, with no tolerance.
/// </summary>
internal static class GroundHash {
  private const ulong Offset = 14695981039346656037UL;

  private const ulong Prime = 1099511628211UL;

  /// <summary>
  /// The cells an outline's bounds cover on a grid, both corners included, clamped to the grid;
  /// empty where the bounds miss it.
  /// </summary>
  /// <param name="toCell">A point of the terrain in cells of the grid, not rounded.</param>
  /// <param name="size">The grid's cells along each axis.</param>
  internal static (int2 Min, int2 Max) Cells(
    IReadOnlyList<float2> polygon,
    Func<float2, float2> toCell,
    int2 size
  ) {
    var low = toCell(polygon.Aggregate(math.min));
    var high = toCell(polygon.Aggregate(math.max));

    return (
      math.max((int2) math.floor(math.min(low, high)), int2.zero),
      math.min((int2) math.ceil(math.max(low, high)), size - 1)
    );
  }

  /// <summary>
  /// Hashes a rectangle of cells into a running hash, by FNV-1a over each cell's two bytes; start
  /// from <see cref="Start"/>.
  /// </summary>
  /// <param name="grid">The grid's raw heights, row after row.</param>
  /// <param name="width">The cells in a row of the grid.</param>
  internal static ulong Add(
    ulong hash,
    NativeArray<ushort> grid,
    int width,
    (int2 Min, int2 Max) cells
  ) {
    for (var z = cells.Min.y; z <= cells.Max.y; z++) {
      var row = z * width;

      for (var x = cells.Min.x; x <= cells.Max.x; x++) {
        var cell = grid[row + x];

        hash = (hash ^ (byte) cell) * GroundHash.Prime;
        hash = (hash ^ (byte) (cell >> 8)) * GroundHash.Prime;
      }
    }

    return hash;
  }

  /// <summary>
  /// The hash no cell has entered yet.
  /// </summary>
  internal static ulong Start => GroundHash.Offset;
}
