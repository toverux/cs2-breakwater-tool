using System;
using Game.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace BreakwaterTool.Scenarios;

/// <summary>
/// Sends the game keys, pointer moves and mouse buttons through the Unity Input System, as the
/// player's keyboard and mouse do, whether or not the game's window has the focus; disposing it
/// releases what it holds and gives the window's focus rules back.
/// An event takes effect on the input system's next update: wait a few frames before reading what
/// it changed, and between a press and its release.
/// </summary>
internal sealed class NativeInput : IDisposable {
  private readonly InputSettings.BackgroundBehavior backgroundBehavior =
    InputSystem.settings.backgroundBehavior;

  // A mouse state event replaces the whole state, so each one carries the buttons held, and each
  // button event the pointer's position.
  private ushort buttons;

  /// <summary>
  /// Lets the input system read the devices with the window in the background, which disables them
  /// otherwise, and the game take the pointer as on its screen.
  /// </summary>
  internal NativeInput() {
    InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
    InputSystem.EnableDevice(Keyboard.current);
    InputSystem.EnableDevice(Mouse.current);
    InputManager.instance.OnFocusChanged(true);
  }

  /// <summary>
  /// Holds the keys given down, and releases every other: no key releases them all.
  /// </summary>
  internal void HoldKeys(params Key[] keys) =>
    InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));

  /// <summary>
  /// Moves the pointer to a point of the screen, in pixels from its bottom left corner, the buttons
  /// held staying held.
  /// </summary>
  internal void MovePointer(Vector2 position) {
    var delta = position - Mouse.current.position.ReadValue();

    // Cohtml hit-tests the UI, and the game updates `mouseOverUI`, only on an event that moves the
    // pointer, which the delta tells and the position alone does not.
    InputSystem.QueueStateEvent(
      Mouse.current,
      new MouseState {
        position = position,
        delta = delta == Vector2.zero ? Vector2.one : delta,
        buttons = this.buttons
      }
    );
  }

  /// <summary>
  /// Presses or releases a mouse button where the pointer is.
  /// </summary>
  internal void SetButton(MouseButton button, bool isDown) {
    var state = new MouseState {
      position = Mouse.current.position.ReadValue(),
      buttons = this.buttons
    }.WithButton(button, isDown);

    this.buttons = state.buttons;

    InputSystem.QueueStateEvent(Mouse.current, state);
  }

  public void Dispose() {
    this.HoldKeys();

    if (this.buttons != 0) {
      this.buttons = 0;

      InputSystem.QueueStateEvent(
        Mouse.current,
        new MouseState {
          position = Mouse.current.position.ReadValue()
        }
      );
    }

    // The devices stay enabled: the input system disables them itself on the window's next loss of
    // focus, where a device disabled here would stay disabled once the window has the focus again.
    InputSystem.settings.backgroundBehavior = this.backgroundBehavior;

    InputManager.instance.OnFocusChanged(Application.isFocused);
  }
}
