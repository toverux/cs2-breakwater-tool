using System.Linq;
using BreakwaterTool.Breakwaters;
using BreakwaterTool.Utils.Writers;
using Unity.Entities;
using Xunit;

namespace BreakwaterTool.Tests.Utils.Writers;

public sealed class LookWriterTests {
  [Fact]
  public void Write_AnyLook_GivesEachTypeOneShape() {
    var writer = new ShapeRecordingJsonWriter();
    var stone = new StoneVerdict(
      new Entity {
        Index = 7,
        Version = 1
      },
      "Boulder",
      8,
      7,
      false
    );

    LookView[] looks = [
      new(BreakwaterParameters.Default, [], [], [], ""),
      new(BreakwaterParameters.Default, [stone], ["thumbnail://one"], ["fallback"], "Coursed"),
      new(
        BreakwaterParameters.Default with {
          Tumble = 1f,
          IsLongAxisAcross = true
        },
        [
          stone,
          stone with {
            Laid = 0,
            IsUnprotectable = true
          }
        ],
        ["thumbnail://one", ""],
        ["fallback", "fallback"],
        ""
      )
    ];

    foreach (var look in looks) {
      new LookWriter().Write(writer, look);
    }

    // The UI caches a type's properties by its name at the first write.
    Assert.Empty(writer.ConflictingTypeNames);
  }

  [Fact]
  public void Write_ALook_NamesEveryParameterAndWhatTheStonesRowReads() {
    var writer = new ShapeRecordingJsonWriter();

    new LookWriter().Write(
      writer,
      new LookView(
        BreakwaterParameters.Default,
        [
          new StoneVerdict(
            new Entity {
              Index = 7,
              Version = 1
            },
            "Boulder",
            8,
            7,
            false
          )
        ],
        ["thumbnail://one"],
        ["fallback"],
        "Coursed"
      )
    );

    // The panel reads these names: the UI's `Look`, `Stone` and `ParameterName` types.
    Assert.Equal(
      "parameters,stones,style",
      writer.Shapes["BreakwaterTool.Look"].Single()
    );

    Assert.Equal(
      string.Join(",", ParameterTable.Names),
      writer.Shapes["BreakwaterTool.Parameters"].Single()
    );

    Assert.Equal(
      "prefab,name,thumbnail,fallback,stones,laid,isUnprotectable",
      writer.Shapes["BreakwaterTool.Stone"].Single()
    );
  }
}
