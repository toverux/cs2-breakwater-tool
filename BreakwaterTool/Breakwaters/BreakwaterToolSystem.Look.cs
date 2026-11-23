using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;

namespace BreakwaterTool.Breakwaters {
  /// <summary>
  /// The look the tool's panel shows and sets: the layout parameters and the stones.
  /// It is the picked breakwater's, which every change lays again, and what the next breakwater
  /// drawn starts from: the one picked last, or what was set since.
  /// The mod's settings keep it from one session to the next.
  /// </summary>
  internal sealed partial class BreakwaterToolSystem {
    /// <summary>
    /// The seconds between two looks at the ground under the picked breakwater.
    /// </summary>
    private const float GroundWatchPeriod = 0.5f;

    // The chosen prefabs, in the order chosen: stones, and random prefabs whose stones are laid.
    private readonly List<Entity> choice = [];

    // Whether the settings' look was read, which waits for a city, where the prefabs are loaded.
    private bool isLookLoaded;

    // The chosen prefabs as a style last set them, which another style replaces; null where none
    // did.
    private Entity[]? styleStones;

    // When the ground under the picked breakwater is next looked at, in real seconds, and whether
    // an edit or a pick asks for a look at once.
    private float groundWatchAt;

    private bool isOutdatedDue;

    /// <summary>
    /// The layout parameters: what a new breakwater takes, and what the picked one has.
    /// </summary>
    internal BreakwaterParameters Parameters { get; private set; } = BreakwaterParameters.Default;

    /// <summary>
    /// The chosen prefabs: what a new breakwater lays, and what the picked one does.
    /// </summary>
    internal IReadOnlyList<Entity> Choice => this.choice;

    /// <summary>
    /// Moves whenever the look, the pick or its toe changes: what the panel's bindings compare to
    /// know whether to write the look again.
    /// </summary>
    internal uint LookRevision { get; private set; }

    /// <summary>
    /// Whether the ground under the picked breakwater changed since its stones were laid.
    /// </summary>
    internal bool IsOutdated { get; private set; }

    /// <summary>
    /// The built-in style whose parameters the look has, whatever its seed and its stones, which
    /// the Stones row shows; null when it has none's.
    /// </summary>
    internal string? Style =>
      BreakwaterStyles.All
        .FirstOrDefault(style =>
          style.Parameters with {
            Seed = this.Parameters.Seed
          } ==
          this.Parameters
        )
        ?.Name;

    /// <summary>
    /// Sets one layout parameter by its name, a flag as 0 or 1; a name or a value the parameters
    /// refuse, or the value they hold, changes nothing. A picked breakwater too slow to lay as a
    /// slider moves is laid once the slider rests, let go.
    /// </summary>
    internal void SetParameter(string name, float value) {
      if (ParameterTable.With(this.Parameters, name, value) is not {} changed ||
        changed == this.Parameters) {
        return;
      }

      if (this.gestures.Picked is {} picked && !this.IsLaidLive(picked)) {
        this.Wait(picked, changed);

        return;
      }

      this.ChangeLook(new BreakwaterChange(Parameters: changed));
    }

    /// <summary>
    /// Fills the parameters from a built-in style, and the stones too while they are still the ones
    /// a style set: stones the player chose by hand stay.
    /// </summary>
    internal void ApplyStyle(string name) {
      if (BreakwaterStyles.Find(name) is not {} style) {
        return;
      }

      var stones = this.breakwaters.Resolve(style.Stones);

      // A style whose stones the game lacks changes the parameters alone.
      var isReplaced = stones.Length > 0 &&
        (this.choice.Count == 0 || this.IsChoice(this.styleStones));

      this.ChangeLook(
        new BreakwaterChange(Parameters: style.Parameters, Choice: isReplaced ? stones : null)
      );

      // A picked breakwater that refused the edit keeps its stones, and what set them.
      if (isReplaced && this.IsChoice(stones)) {
        this.styleStones = stones;
      }
    }

    /// <summary>
    /// Draws another arrangement of the same look.
    /// </summary>
    internal void Reshuffle() =>
      this.ChangeLook(
        new BreakwaterChange(
          Parameters: this.Parameters with {
            Seed = unchecked(this.Parameters.Seed + 1)
          }
        )
      );

    /// <summary>
    /// Clears the plants inside the picked breakwater's outline; Revert brings them back.
    /// </summary>
    internal void ClearPickedVegetation() {
      if (this.gestures.Picked is not {} picked) {
        return;
      }

      var before = this.snapshot ?? this.breakwaters.Snapshot(picked);
      var cleared = this.breakwaters.ClearVegetation(picked);

      if (cleared.Length == 0 || before is null) {
        this.PlaySound(static sounds => sounds.m_PlaceBuildingFailSound);

        return;
      }

      before.Cleared.AddRange(cleared);
      this.snapshot = before;
      this.PlaySound(static sounds => sounds.m_BulldozeSound);
    }

    /// <summary>
    /// How each chosen prefab fares with the look's parameters, for the panel's Stones row.
    /// </summary>
    internal StoneVerdict[] JudgeStones() => this.breakwaters.Judge(this.choice, this.Parameters);

    /// <summary>
    /// Changes the look: the picked breakwater is laid again with it and the panel shows what its
    /// record then holds; with none picked, the next breakwater drawn takes it.
    /// </summary>
    private void ChangeLook(BreakwaterChange change) {
      if (this.gestures.Picked is {} picked) {
        this.EditPicked(picked, change);

        return;
      }

      this.Parameters = change.Parameters ?? this.Parameters;

      if (change.Choice is {} stones) {
        this.choice.Clear();
        this.choice.AddRange(stones);
      }

      this.LookRevision++;
    }

    /// <summary>
    /// Takes a breakwater's look as the tool's own.
    /// </summary>
    private void ShowLook(BreakwaterState state) {
      this.Parameters = state.Parameters;
      this.choice.Clear();
      this.choice.AddRange(state.Choice);
      this.LookRevision++;
    }

    /// <summary>
    /// Whether the chosen prefabs are a set of stones, in any order.
    /// </summary>
    private bool IsChoice(Entity[]? stones) =>
      stones is { Length: > 0 } &&
      stones.Length == this.choice.Count &&
      stones.All(this.choice.Contains);

    /// <summary>
    /// Takes the chosen prefabs as a style's where they are a built-in style's set: a breakwater's
    /// record, as the settings, keeps no word of who chose them.
    /// </summary>
    private void ReadStyleStones() =>
      this.styleStones = BreakwaterStyles.All
        .Select(style => this.breakwaters.Resolve(style.Stones))
        .FirstOrDefault(this.IsChoice);

    /// <summary>
    /// Looks at the ground under the picked breakwater twice a second, and at once after a pick or
    /// an edit: whether it changed since the stones were laid, and whether it holds a mound to
    /// dress. Other tools reshape the terrain and the water rises and falls while the breakwater
    /// stays picked.
    /// </summary>
    // [PERF] Twice a second, in place of every frame: the look reads every height cell under the
    // outline's bounds, and samples the ground and the water across it.
    private void WatchGround() {
      // A press on the terrain leaves the pick for a new outline without the tool's unpick.
      if (this.gestures.Picked is not {} picked) {
        this.IsOutdated = false;

        return;
      }

      if (this.gestures.Held >= 0) {
        return;
      }

      var now = UnityEngine.Time.realtimeSinceStartup;

      if (!this.isOutdatedDue && now < this.groundWatchAt) {
        return;
      }

      this.isOutdatedDue = false;
      this.groundWatchAt = now + BreakwaterToolSystem.GroundWatchPeriod;
      this.IsOutdated = this.breakwaters.IsOutdated(picked);
      this.JudgePickedGround();
    }

    /// <summary>
    /// Reads the look the settings keep, once a city gives the prefabs; the default style's where
    /// they keep none.
    /// </summary>
    private void LoadLook() {
      if (this.isLookLoaded) {
        return;
      }

      this.isLookLoaded = true;

      var kept = Mod.Settings.LastLook.Split('|');

      this.Parameters = ParameterTable.Read(kept[0]);
      this.choice.Clear();

      if (kept.Length > 1) {
        this.choice.AddRange(
          this.breakwaters
            .Resolve(kept[1].Split([','], StringSplitOptions.RemoveEmptyEntries))
            .Where(this.breakwaters.CanLay)
        );
      }

      // Stones kept that the game no longer holds leave the default style's.
      if (this.choice.Count == 0) {
        this.choice.AddRange(this.breakwaters.Resolve(BreakwaterStyles.DefaultStyle.Stones));
      }

      this.ReadStyleStones();
      this.LookRevision++;
    }

    /// <summary>
    /// Keeps the look in the settings, as the tool is left.
    /// </summary>
    private void SaveLook() {
      if (!this.isLookLoaded) {
        return;
      }

      var look = ParameterTable.Write(this.Parameters) +
        "|" +
        string.Join(",", this.choice.Select(this.breakwaters.IdOf));

      if (look == Mod.Settings.LastLook) {
        return;
      }

      Mod.Settings.LastLook = look;
      Mod.Settings.ApplyAndSave();
    }
  }
}
