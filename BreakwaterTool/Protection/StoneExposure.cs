using Game.Objects;

namespace BreakwaterTool.Protection;

/// <summary>
/// Whether the game would move or hide an object of a prefab on its own, from its geometry flags.
/// The game hides an overlapped object only when it is overridable, and <c>GroundHeightSystem</c>
/// re-seats one on a terrain change when it has a base, or is overridable, a marker or brushable
/// without deleting what it overrides.
/// </summary>
internal static class StoneExposure {
  internal static bool IsExposed(GeometryFlags flags) =>
    (flags & GeometryFlags.HasBase) != 0 ||
    ((flags & GeometryFlags.DeleteOverridden) == 0 &&
      (flags & (GeometryFlags.Overridable | GeometryFlags.Marker | GeometryFlags.Brushable)) != 0);
}
