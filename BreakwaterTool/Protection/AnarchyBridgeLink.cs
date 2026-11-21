using System;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using Transform = Game.Objects.Transform;

namespace BreakwaterTool.Protection;

/// <summary>
/// Anarchy's bridge for other mods, reached by reflection and bound to typed delegates.
/// Its signatures use game types only, so a call costs what a direct call does, and the mod builds
/// and loads without Anarchy: a compile-time reference would fail with a
/// <see cref="TypeLoadException"/> wherever a method touching the bridge is compiled.
/// </summary>
internal sealed class AnarchyBridgeLink {
  private const string BridgeType = "Anarchy.Bridge.AnarchyBridge";

  private const string ModType = "Anarchy.AnarchyMod";

  private readonly Func<Entity, bool> tryAddOverridePrevention;

  private readonly Func<Entity, Transform, bool> tryAddTransformLock;

  // Anarchy's mod object, its internal settings object and the editor option on it, each null when
  // Anarchy changed it.
  private readonly PropertyInfo? modInstance;

  private readonly PropertyInfo? modSettings;

  private readonly PropertyInfo? editorOption;

  private AnarchyBridgeLink(Assembly assembly, Type bridge) {
    this.Version = assembly.GetName().Version?.ToString() ?? "unknown";

    this.tryAddOverridePrevention = AnarchyBridgeLink.Bind<Func<Entity, bool>>(
      bridge,
      "TryAddAnarchyComponent",
      [typeof(Entity)]
    );

    this.tryAddTransformLock = AnarchyBridgeLink.Bind<Func<Entity, Transform, bool>>(
      bridge,
      "TryAddTransformLockComponent",
      [typeof(Entity), typeof(Transform)]
    );

    this.OverridePreventionType = AnarchyBridgeLink.Bind<Func<ComponentType>>(
      bridge,
      "GetAnarchyComponentType",
      []
    )();

    this.TransformLockType = AnarchyBridgeLink.Bind<Func<ComponentType>>(
      bridge,
      "GetTransformLockComponentType",
      []
    )();

    var mod = assembly.GetType(AnarchyBridgeLink.ModType);

    this.modInstance = mod?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
    this.modSettings = mod?.GetProperty("Settings", BindingFlags.NonPublic | BindingFlags.Instance);
    this.editorOption = this.modSettings?.PropertyType.GetProperty("PreventOverrideInEditor");
  }

  /// <summary>
  /// The version of the Anarchy assembly the bridge was found in.
  /// </summary>
  internal string Version { get; }

  /// <summary>
  /// The component of Anarchy's override prevention, for adding it in bulk to the stones of a
  /// prefab the bridge accepted once.
  /// </summary>
  internal ComponentType OverridePreventionType { get; }

  /// <summary>
  /// The component of Anarchy's transform lock.
  /// </summary>
  internal ComponentType TransformLockType { get; }

  /// <summary>
  /// Anarchy's option <i>Anarchy Components In Editor</i>, or null when it cannot be read.
  /// Without it, Anarchy's systems leave protected objects to the game in the editor.
  /// The bridge has no accessor for it: it is read from Anarchy's settings object.
  /// </summary>
  internal bool? PreventOverrideInEditor {
    get {
      try {
        return this.Settings is {} settings ? this.editorOption?.GetValue(settings) as bool? : null;
      }
      catch (TargetInvocationException) {
        return null;
      }
    }
    #if DEBUG
    // For the scenarios, which lay with the option off, then on.
    set {
      if (this.Settings is {} settings) {
        this.editorOption?.SetValue(settings, value);
      }
    }
    #endif
  }

  private object? Settings =>
    this.modInstance?.GetValue(null) is {} mod ? this.modSettings?.GetValue(mod) : null;

  /// <summary>
  /// Finds the bridge among the loaded assemblies.
  /// </summary>
  /// <param name="link">The bridge, when found whole.</param>
  /// <returns>Why the bridge cannot be used, or null when it can.</returns>
  internal static string? TryResolve(out AnarchyBridgeLink? link) {
    link = null;

    var (assembly, bridge) = AppDomain.CurrentDomain.GetAssemblies()
      .Select(static candidate => (candidate, candidate.GetType(AnarchyBridgeLink.BridgeType)))
      .FirstOrDefault(static pair => pair.Item2 is not null);

    if (bridge is null) {
      return "Anarchy is not loaded";
    }

    try {
      link = new AnarchyBridgeLink(assembly, bridge);

      return null;
    }
    // Binding fails on a changed signature, and the type getters run Anarchy's own code, which may
    // throw anything.
    catch (Exception ex) {
      return $"Anarchy {assembly.GetName().Version} has no usable bridge: {ex.Message}";
    }
  }

  internal bool TryAddOverridePrevention(Entity stone) => this.tryAddOverridePrevention(stone);

  internal bool TryAddTransformLock(Entity stone, Transform transform) =>
    this.tryAddTransformLock(stone, transform);

  private static TDelegate Bind<TDelegate>(Type bridge, string name, Type[] parameters)
    where TDelegate : Delegate {
    var method = bridge.GetMethod(
        name,
        BindingFlags.Public | BindingFlags.Static,
        null,
        parameters,
        null
      ) ??
      throw new MissingMethodException(bridge.FullName, name);

    // Throws an ArgumentException when the return type changed.
    return (TDelegate) Delegate.CreateDelegate(typeof(TDelegate), method);
  }
}
