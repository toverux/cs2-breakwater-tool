namespace BreakwaterTool.Protection;

/// <summary>
/// What laying a stone prefab takes, judged once per prefab before the layout.
/// </summary>
internal enum PrefabCare {
  /// <summary>
  /// The game would hide or re-seat its stones, and the protection covers them.
  /// </summary>
  Protect,

  /// <summary>
  /// The game neither hides nor re-seats its stones: they need no protection.
  /// </summary>
  Inert,

  /// <summary>
  /// The game would hide or re-seat its stones, and the protection cannot cover them: the layout
  /// leaves the prefab out.
  /// </summary>
  Unprotectable
}
