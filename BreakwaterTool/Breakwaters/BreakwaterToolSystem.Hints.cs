using System.Linq;
using BreakwaterTool.Gestures;
using Game.Input;
using Unity.Entities;
using UnityEngine;

namespace BreakwaterTool.Breakwaters {
  /// <summary>
  /// The hints bar: the vanilla area tool's labels on the tool's three actions, as the gesture
  /// interpreter says what each would do at the pointer.
  /// </summary>
  internal sealed partial class BreakwaterToolSystem {
    /// <summary>
    /// The mod's own labels, which the source locale names under <c>Common.ACTION[label]</c>.
    /// </summary>
    internal const string SetToeLabel = "BreakwaterTool.SetToe";

    internal const string RemoveToeLabel = "BreakwaterTool.RemoveToe";

    // The game's labels, read in OnCreate: hence mutable; null where the game has none.
    private IProxyAction? addNode;

    private IProxyAction? completeArea;

    private IProxyAction? createArea;

    private IProxyAction? moveNode;

    private IProxyAction? insertNode;

    private IProxyAction? deleteNode;

    private IProxyAction? undoNode;

    private IProxyAction? discardMove;

    private IProxyAction? discardInsert;

    private IProxyAction? selectArea;

    private IProxyAction? deselectArea;

    // The mod's own labels, on the action of one of the game's.
    private IProxyAction? setToe;

    private IProxyAction? removeToe;

    private void OnCreateHints() {
      // The asset the game's input manager loads its tool labels from, which it keeps internal.
      var labels = Resources.Load<UIInputActionCollection>("Input/Tool Input Actions");

      this.addNode = this.Label(labels, "Add Area Node");
      this.completeArea = this.Label(labels, "Complete Area");
      this.createArea = this.Label(labels, "Create Area");
      this.moveNode = this.Label(labels, "Move Area Node");
      this.insertNode = this.Label(labels, "Insert Area Node");
      this.deleteNode = this.Label(labels, "Delete Area Node");
      this.undoNode = this.Label(labels, "Undo Area Node");
      this.discardMove = this.Label(labels, "Discard Move Area Node");
      this.discardInsert = this.Label(labels, "Discard Insert Area Node");
      this.selectArea = this.Label(labels, "Select Area");
      this.deselectArea = this.Label(labels, "Deselect Area");
      this.setToe = this.Label(labels, "Insert Area Node", BreakwaterToolSystem.SetToeLabel);
      this.removeToe = this.Label(labels, "Insert Area Node", BreakwaterToolSystem.RemoveToeLabel);
    }

    /// <summary>
    /// A label of the game's, by its name; null where the game has none of that name, or no
    /// collection of labels, which leaves the action without a label and the mod loaded.
    /// </summary>
    private IProxyAction? Label(UIInputActionCollection? labels, string name) {
      // GetActionState returns null for a name it lacks (decompile: UIInputActionCollection).
      var state = labels?.GetActionState(name, nameof(BreakwaterToolSystem));

      this.OnLabelRead(name, state);

      return state;
    }

    /// <summary>
    /// A label of the mod's, on the action of one of the game's labels and shown as it is: the
    /// hints bar reads its text from the locale key <c>Common.ACTION[text]</c>.
    /// </summary>
    private IProxyAction? Label(UIInputActionCollection? labels, string name, string text) {
      if (labels?.m_InputActions.FirstOrDefault(action => action.aliasName == name
        ) is not {} like) {
        this.OnLabelRead(text, null);

        return null;
      }

      var state = like.GetState(
        $"{text} ({nameof(BreakwaterToolSystem)})",
        (source, action, _, transform) => new DisplayNameOverride(
          source,
          action,
          text,
          like.displayPriority,
          like.m_DisplayMask,
          transform
        )
      );

      this.OnLabelRead(text, state);

      return state;
    }

    /// <summary>
    /// Each label read, which the Debug build keeps for the scenarios to check.
    /// </summary>
    partial void OnLabelRead(string name, IProxyAction? state);

    private void ShowHints(GestureTarget<Entity> target) {
      var isHeld = this.gestures.Held >= 0;

      this.applyActionOverride = this.Hint switch {
        GestureHint.PlaceFirst or GestureHint.PlaceNext or GestureHint.PlaceOrClose => this.addNode,
        GestureHint.Close => this.completeArea,
        GestureHint.Pick => this.selectArea,
        GestureHint.Move or GestureHint.MoveOrRemove or GestureHint.Release => this.moveNode,
        GestureHint.Insert => this.insertNode,
        GestureHint.StartAnother => this.createArea,
        GestureHint.ToggleToe => target.Index >= this.toe.Length || !this.toe[target.Index]
          ? this.setToe
          // The press refuses the last toe edge.
          : this.toe.Count(static isToe => isToe) > 1
            ? this.removeToe
            : null,
        _ => null
      };

      this.applyAction.shouldBeEnabled = true;

      this.secondaryApplyActionOverride = (this.gestures.Mode, this.Hint) switch {
        (_, _) when isHeld => null,
        (GestureMode.Drawing, _) => this.undoNode,
        (_, GestureHint.MoveOrRemove) => this.deleteNode,
        // The secondary button is refused on a vertex of three.
        (_, GestureHint.Move) => null,
        (GestureMode.Editing, _) => this.deselectArea,
        _ => null
      };

      this.secondaryApplyAction.shouldBeEnabled = true;

      this.cancelActionOverride = isHeld
        ? this.gestures.IsHeldInserted ? this.discardInsert : this.discardMove
        : null;

      this.cancelAction.shouldBeEnabled = isHeld;
    }
  }
}
