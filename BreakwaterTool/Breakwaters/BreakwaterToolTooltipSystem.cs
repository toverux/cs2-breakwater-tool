using System.Collections.Generic;
using BreakwaterTool.Layout;
using Game.Tools;
using Game.UI.Localization;
using Game.UI.Tooltip;

namespace BreakwaterTool.Breakwaters {
  /// <summary>
  /// The tool's tooltips, as the game's own tools show theirs: at the pointer, why the last gesture
  /// laid nothing, and a mound to shape first under a level or drowned outline; in the world, the
  /// value of each angle the overlay shows, as the area tool's guide lines do.
  /// The hints bar already says what the next gesture does.
  /// </summary>
  internal sealed partial class BreakwaterToolTooltipSystem : TooltipSystemBase {
    // One group per angle shown at once, grown as needed and reused across frames.
    private readonly List<TooltipGroup> angleGroups = [];

    // Created in OnCreate, which the game runs before any other member: hence mutable, and null!
    // until then.
    private ToolSystem toolSystem = null!;

    private BreakwaterToolSystem tool = null!;

    private StringTooltip refusal = null!;

    private StringTooltip ground = null!;

    protected override void OnCreate() {
      base.OnCreate();

      this.toolSystem = this.World.GetOrCreateSystemManaged<ToolSystem>();
      this.tool = this.World.GetOrCreateSystemManaged<BreakwaterToolSystem>();

      this.refusal = new StringTooltip {
        path = "breakwaterToolRefusal",
        color = TooltipColor.Warning
      };

      this.ground = new StringTooltip {
        path = "breakwaterToolGround",
        color = TooltipColor.Warning
      };
    }

    protected override void OnUpdate() {
      if (this.toolSystem.activeTool != this.tool) {
        return;
      }

      if (this.tool.Refusal is {} refusal) {
        this.refusal.value = refusal;
        this.AddMouseTooltip(this.refusal);
      }

      if (this.tool.Ground is not GroundVerdict.Shaped) {
        this.ground.value = LocalizedString.Id($"BreakwaterTool.Tool.GROUND[{this.tool.Ground}]");
        this.AddMouseTooltip(this.ground);
      }

      this.ShowAngles();
    }

    private void ShowAngles() {
      var angles = this.tool.Angles;

      for (var i = 0; i < angles.Count; i++) {
        if (this.angleGroups.Count <= i) {
          this.angleGroups.Add(
            new TooltipGroup {
              path = $"breakwaterToolAngle{i}",
              horizontalAlignment = TooltipGroup.Alignment.Center,
              verticalAlignment = TooltipGroup.Alignment.Center,
              category = TooltipGroup.Category.Network,
              children = {
                new FloatTooltip {
                  icon = "Media/Glyphs/Angle.svg",
                  unit = "angle"
                }
              }
            }
          );
        }

        var group = this.angleGroups[i];
        var position = TooltipSystemBase.WorldToTooltipPos(angles[i].At, out _);

        if (!group.position.Equals(position)) {
          group.position = position;
          group.SetChildrenChanged();
        }

        ((FloatTooltip) group.children[0]).value = angles[i].Degrees;
        this.AddGroup(group);
      }
    }
  }
}
