using System.Collections.Generic;
using UnityEngine;

namespace BreakwaterTool.Scenarios;

/// <summary>
/// A panel over the game while scenarios run: the scenario and its time, a bar of its tests, a bar
/// of the current test's steps where it has some, the counts of checks, and the last checks.
/// Drawn with Unity's immediate mode GUI, which needs no UI of the game's, in the game and in the
/// editor alike.
/// </summary>
internal sealed class ProgressOverlay : MonoBehaviour {
  /// <summary>
  /// The panel's bottom, as a share of the screen's height: above the game's toolbar.
  /// </summary>
  private const float Bottom = 0.84f;

  /// <summary>
  /// How long the panel stays once the scenario ended, in seconds.
  /// </summary>
  private const float Linger = 10f;

  private const int History = 5;

  private static readonly Color Panel = new(0.08f, 0.1f, 0.13f, 0.86f);

  private static readonly Color Track = new(1f, 1f, 1f, 0.12f);

  private static readonly Color Accent = new(0.36f, 0.68f, 0.95f);

  private static readonly Color Passed = new(0.4f, 0.8f, 0.45f);

  private static readonly Color Failed = new(0.95f, 0.38f, 0.35f);

  private static ProgressOverlay? current;

  // Each check's outcome and text, the latest last, at most History of them.
  private readonly Queue<(bool Passed, string Text)> checks = new();

  // The panel's state, set by the scenario through the methods below, read each frame by OnGUI.
  private string scenario = string.Empty;

  private string test = string.Empty;

  private int testIndex;

  private int testCount;

  private string step = string.Empty;

  private int stepIndex;

  private int stepCount;

  private int passed;

  private int failed;

  private float started;

  private float ended = float.NaN;

  // Built on the first draw: the GUI skin only exists while the GUI draws.
  private GUIStyle? text;

  private GUIStyle? title;

  private GUIStyle? right;

  private Texture2D? white;

  /// <summary>
  /// The panel of the scenario that starts, replacing any earlier one.
  /// </summary>
  internal static ProgressOverlay Start(string scenario, int testCount) {
    if (ProgressOverlay.current != null) {
      Object.Destroy(ProgressOverlay.current.gameObject);
    }

    var holder = new GameObject(nameof(ProgressOverlay));

    // A scenario loads maps and cities: the panel outlives each load.
    Object.DontDestroyOnLoad(holder);

    var overlay = holder.AddComponent<ProgressOverlay>();

    overlay.scenario = scenario;
    overlay.testCount = testCount;
    overlay.started = Time.realtimeSinceStartup;

    return ProgressOverlay.current = overlay;
  }

  internal void StartTest(int index, string name) {
    this.testIndex = index;
    this.test = name;
    this.stepCount = 0;
  }

  /// <summary>
  /// A step of the current test, such as one site of several, shown on a bar of its own.
  /// </summary>
  internal void Step(string name, int index, int count) {
    this.step = name;
    this.stepIndex = index;
    this.stepCount = count;
  }

  internal void Report(bool isPassed, string check) {
    this.passed += isPassed ? 1 : 0;
    this.failed += isPassed ? 0 : 1;
    this.checks.Enqueue((isPassed, check));

    while (this.checks.Count > ProgressOverlay.History) {
      this.checks.Dequeue();
    }
  }

  internal void End() {
    this.ended = Time.realtimeSinceStartup;
    this.test = "Finished";
    this.stepCount = 0;
  }

  private void Update() {
    if (Time.realtimeSinceStartup - this.ended > ProgressOverlay.Linger) {
      Object.Destroy(this.gameObject);
    }
  }

  private void OnDestroy() {
    if (this.white != null) {
      Object.Destroy(this.white);
    }
  }

  private void OnGUI() {
    var unit = Screen.height / 60f;

    this.text ??= new GUIStyle(GUI.skin.label) {
      fontSize = (int) unit,
      richText = true,
      wordWrap = false,
      clipping = TextClipping.Clip
    };

    this.title ??= new GUIStyle(this.text) {
      fontStyle = FontStyle.Bold
    };
    this.right ??= new GUIStyle(this.text) {
      alignment = TextAnchor.MiddleRight
    };

    if (this.white == null) {
      this.white = new Texture2D(1, 1);
      this.white.SetPixel(0, 0, Color.white);
      this.white.Apply();
    }

    var width = unit * 34f;
    var line = unit * 1.6f;
    var lines = 4 + (this.stepCount > 0 ? 1 : 0) + this.checks.Count;
    var height = (line * lines) + (unit * 1.6f);
    var panel = new Rect(unit, (Screen.height * ProgressOverlay.Bottom) - height, width, height);

    this.Fill(panel, ProgressOverlay.Panel);
    this.Fill(new Rect(panel.x, panel.y, unit * 0.3f, panel.height), ProgressOverlay.Accent);

    var row = new Rect(panel.x + unit, panel.y + (unit * 0.8f), width - (unit * 2f), line);
    var now = float.IsNaN(this.ended) ? Time.realtimeSinceStartup : this.ended;
    var seconds = (int) (now - this.started);

    GUI.Label(row, $"{this.scenario}", this.title);
    GUI.Label(row, $"<color=#9aa>{seconds / 60}:{seconds % 60:00}</color>", this.right);
    row.y += line;

    this.Bar(row, unit, this.testIndex, this.testCount, ProgressOverlay.Accent);
    GUI.Label(row, $"{this.testIndex} / {this.testCount}", this.right);
    row.y += line;

    GUI.Label(row, this.test, this.text);
    row.y += line;

    if (this.stepCount > 0) {
      this.Bar(row, unit, this.stepIndex, this.stepCount, ProgressOverlay.Accent * 0.8f);
      GUI.Label(
        row,
        $"<color=#9aa>{this.step}</color>  {this.stepIndex} / {this.stepCount}",
        this.right
      );
      row.y += line;
    }

    this.Dot(row, unit, ProgressOverlay.Passed);
    GUI.Label(
      new Rect(row.x + (unit * 1.2f), row.y, row.width, row.height),
      $"{this.passed} passed",
      this.text
    );
    this.Dot(
      new Rect(row.x + (unit * 9f), row.y, row.width, row.height),
      unit,
      ProgressOverlay.Failed
    );
    GUI.Label(
      new Rect(row.x + (unit * 10.2f), row.y, row.width, row.height),
      $"{this.failed} failed",
      this.text
    );
    row.y += line;

    foreach (var (isPassed, check) in this.checks) {
      this.Dot(row, unit * 0.7f, isPassed ? ProgressOverlay.Passed : ProgressOverlay.Failed);
      GUI.Label(
        new Rect(row.x + (unit * 1.2f), row.y, row.width - (unit * 1.2f), row.height),
        $"<color=#cdd>{check}</color>",
        this.text
      );
      row.y += line;
    }
  }

  /// <summary>
  /// A bar filled to a share, on the left two thirds of a row.
  /// </summary>
  private void Bar(Rect row, float unit, int done, int count, Color colour) {
    var track = new Rect(
      row.x,
      row.y + (row.height / 2f) - (unit * 0.3f),
      row.width * 0.66f,
      unit * 0.6f
    );

    this.Fill(track, ProgressOverlay.Track);
    this.Fill(
      new Rect(
        track.x,
        track.y,
        track.width * (count > 0 ? (float) done / count : 0f),
        track.height
      ),
      colour
    );
  }

  private void Dot(Rect row, float size, Color colour) =>
    this.Fill(new Rect(row.x, row.y + ((row.height - size) / 2f), size, size), colour);

  private void Fill(Rect area, Color colour) {
    var previous = GUI.color;

    GUI.color = colour;
    GUI.DrawTexture(area, this.white);
    GUI.color = previous;
  }
}
