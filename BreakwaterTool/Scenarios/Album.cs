using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using BreakwaterTool.Scenarios.Ground;
using Game.Rendering;
using Game.Simulation;
using Game.UI;
using Game.UI.Menu;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BreakwaterTool.Scenarios;

/// <summary>
/// The proving ground's album, for the by-eye judgments: per site, its approved picture beside a
/// shot of it in the game, on one page in a folder of the run's own.
/// </summary>
internal sealed class Album {
  private const int Width = 1600;

  private const int Height = 900;

  private readonly StringBuilder page = new();

  // The approved pictures' folder, as a file URI: the page links them where the tests keep them.
  private readonly string pictures;

  internal Album([CallerFilePath] string source = "") {
    this.Folder = Path.Combine(
      Application.persistentDataPath,
      "ModsData",
      nameof(BreakwaterTool),
      "Album",
      DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture)
    );

    Directory.CreateDirectory(this.Folder);

    // This file is in BreakwaterTool/Scenarios/, the pictures in BreakwaterTool.Tests/.
    var root = source.Replace('\\', '/');

    root = root[..root.LastIndexOf("/BreakwaterTool/", StringComparison.Ordinal)];
    this.pictures = $"file://{(root.StartsWith("/", StringComparison.Ordinal) ? "" : "/")}{root}" +
      "/BreakwaterTool.Tests/Layout/Pictures";

    this.page.Append(
      "<!doctype html>\n<meta charset=\"utf-8\">\n<title>Proving ground album</title>\n" +
      "<style>body{font:14px sans-serif;background:#16191d;color:#dde}" +
      "section{display:flex;gap:12px;align-items:flex-start;margin:24px 0}" +
      "section div{width:220px}img{height:450px;background:#dde8f0}</style>\n" +
      $"<h1>Proving ground, {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}</h1>\n"
    );
  }

  internal string Folder { get; }

  /// <summary>
  /// Aims the camera at a site from its sea side, looking inland and down at an angle: embed and
  /// tilt only read obliquely.
  /// At this pitch and twice the site's size away, the game's field of view, some 32° high, holds
  /// the site's near edge, which perspective pushes furthest from the frame's middle.
  /// </summary>
  internal static void Aim(World world, ProvingSite site) {
    var min = site.Outline.Aggregate(math.min);
    var max = site.Outline.Aggregate(math.max);
    var middle = (min + max) / 2f;
    var camera = world.GetExistingSystemManaged<CameraUpdateSystem>().activeCameraController;

    camera.pivot = new Vector3(middle.x, Terraform.Height(world, middle), middle.y);
    camera.zoom = math.max(60f, math.cmax(max - min) * 2f);
    camera.rotation = new Vector3(45f, 0f, 0f);
  }

  /// <summary>
  /// The sea's depth at a point, as the game's systems read it.
  /// </summary>
  internal static float SeaDepth(World world, float2 at) {
    var surface = world.GetExistingSystemManaged<WaterSystem>().GetSurfaceData(out var deps);

    deps.Complete();

    return WaterUtils.SampleDepth(ref surface, new float3(at.x, 0f, at.y));
  }

  /// <summary>
  /// Shoots what the camera sees, without the game's UI, into the album beside the site's picture.
  /// </summary>
  internal void Shoot(ProvingSite site) {
    var target = ScreenCaptureHelper.CreateRenderTarget(
      $"{nameof(Album)}-{site.Name}",
      Album.Width,
      Album.Height
    );

    ScreenCaptureHelper.CaptureScreenshot(
      Camera.main,
      target,
      new MenuHelpers.SaveGamePreviewSettings {
        stylized = false
      }
    );

    var image = new Texture2D(Album.Width, Album.Height, TextureFormat.RGBA32, false);
    var active = RenderTexture.active;

    RenderTexture.active = target;
    image.ReadPixels(new Rect(0, 0, Album.Width, Album.Height), 0, 0);
    image.Apply();
    RenderTexture.active = active;

    File.WriteAllBytes(Path.Combine(this.Folder, $"{site.Name}.png"), image.EncodeToPNG());

    Object.Destroy(image);
    target.Release();
    Object.Destroy(target);

    this.page.Append(
      $"<section><div><h2>{site.Name}</h2><p>{WebUtility.HtmlEncode(site.Shows)}</p></div>" +
      $"<img src=\"{this.pictures}/{site.Name}.svg\" alt=\"no approved picture\">" +
      $"<img src=\"{site.Name}.png\"></section>\n"
    );
  }

  /// <summary>
  /// Writes the page, and gives its path.
  /// </summary>
  internal string Close() {
    var path = Path.Combine(this.Folder, "index.html");

    File.WriteAllText(path, this.page.ToString());

    return path;
  }
}
