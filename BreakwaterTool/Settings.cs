using BreakwaterTool.Utils;
using Colossal.IO.AssetDatabase;
using Colossal.UI.Binding;
using Game.Input;
using Game.Modding;
using Game.Settings;
using JetBrains.Annotations;

namespace BreakwaterTool;

[FileLocation($"ModsSettings/{nameof(BreakwaterTool)}/{nameof(BreakwaterTool)}")]
[SettingsUIGroupOrder(Settings.GroupTool, Settings.GroupAdvanced)]
[SettingsUIShowGroupName(Settings.GroupTool, Settings.GroupAdvanced)]
[SettingsUIKeyboardAction(
  nameof(Settings.KeyBindingToggleTool),
  Usages.kDefaultUsage,
  Usages.kToolUsage,
  Usages.kEditorUsage
)]
public sealed class Settings : ModSetting, IJsonWritable {
  private const string GroupTool = "Tool";

  private const string GroupAdvanced = "Advanced";

  public Settings(IMod mod) : base(mod) {
    this.SetDefaults();
  }

  /// <summary>
  /// Keyboard shortcut opening and leaving Breakwater Tool, in a game and in the editor.
  /// </summary>
  [SettingsUISection(Settings.GroupTool)]
  [SettingsUIKeyboardBinding(BindingKeyboard.B, nameof(Settings.KeyBindingToggleTool), ctrl: true)]
  public ProxyBinding KeyBindingToggleTool { get; set; }

  /// <summary>
  /// The look the tool set last, its layout parameters and its stones, which the next session's
  /// first breakwater starts from; the tool writes and reads it, and the options screen shows
  /// nothing of it.
  /// </summary>
  [SettingsUIHidden]
  public string LastLook { get; set; } = "";

  [SettingsUIButton]
  [SettingsUIConfirmation]
  [SettingsUISection(Settings.GroupAdvanced)]
  [UsedImplicitly]
  public bool ResetSettings {
    // ReSharper disable once ValueParameterNotUsed
    set {
      this.SetDefaults();
      // SetDefaults leaves key bindings alone: only this restores them to their attribute defaults
      // (decompile, ModSetting.ResetKeyBindings).
      this.ResetKeyBindings();
      this.ApplyAndSave();
    }
  }

  /// <summary>
  /// Applies default values to every setting.
  /// Do NOT initialize the properties inline instead, as this method is also the reset path of
  /// <see cref="ResetSettings"/>.
  /// </summary>
  public override void SetDefaults() {
    // The key binding is restored by ResetKeyBindings.
    this.LastLook = "";
  }

  /// <summary>
  /// Restores the default of every string setting that came back null from the settings file.
  /// Call it right after loading, passing an instance that has only been through
  /// <see cref="SetDefaults"/>; <see cref="NullStrings.Restore{T}"/> tells why.
  /// </summary>
  internal void RestoreNullStrings(Settings defaults) => NullStrings.Restore(this, defaults);

  internal Settings Clone() => (Settings) this.MemberwiseClone();

  /// <summary>
  /// The settings the UI reads, through the common UI system's settings binding: a curated subset,
  /// each setting the UI needs written as a camelCase property between the type markers.
  /// </summary>
  void IJsonWritable.Write(IJsonWriter writer) {
    writer.TypeBegin(this.GetType().FullName);
    writer.TypeEnd();
  }
}
