using BreakwaterTool.Breakwaters;
using Game.UI.Editor;
using Game.UI.Localization;

namespace BreakwaterTool.Systems {
  /// <summary>
  /// The tool's panel in the editor, empty as the vanilla bulldozer's: the editor shows the tool
  /// options only in the footer of an open panel, so this one opens with the tool to hold them.
  /// The editor panel system updates it while it is open.
  /// </summary>
  internal sealed partial class BreakwaterToolPanelSystem : EditorPanelSystemBase {
    protected override void OnCreate() {
      base.OnCreate();

      this.children = [];
      this.title = LocalizedString.Id($"Editor.TOOL[{BreakwaterToolSystem.Id}]");
    }
  }
}
