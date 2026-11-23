using System;
using System.Collections.Generic;
using System.Reflection;
using BreakwaterTool.Breakwaters;
using Game.Prefabs;
using Xunit;

namespace BreakwaterTool.Tests.Breakwaters;

/// <summary>
/// Find It's multi-select contract, which Find It looks up on the active tool by reflection, by
/// these names and signatures: a rename or a changed signature leaves Ctrl-click in Find It
/// switching to the prefab's own tool, with nothing failing anywhere.
/// The names are written out, as Find It holds them, so that a rename here fails the test.
/// </summary>
public sealed class FindItContractTests {
  private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;

  [Fact]
  public void BreakwaterToolSystem_HasTheMethodsFindItLooksUp() {
    var type = typeof(BreakwaterToolSystem);

    var select = type.GetMethod("SelectPrefab", FindItContractTests.Flags, [typeof(PrefabBase)]);
    var deselect = type.GetMethod(
      "DeselectPrefab",
      FindItContractTests.Flags,
      [typeof(PrefabBase)]
    );
    var selected = type.GetMethod("GetSelectedPrefabs", FindItContractTests.Flags, Type.EmptyTypes);

    Assert.NotNull(select);
    Assert.NotNull(deselect);
    Assert.NotNull(selected);
    Assert.True(typeof(IEnumerable<PrefabBase>).IsAssignableFrom(selected.ReturnType));
  }
}
