using System;
using BreakwaterTool.Layout;
using Unity.Mathematics;

namespace BreakwaterTool.Tests.Layout;

/// <summary>
/// A terrain given as a function, and the distance field filled by the job's body in a loop.
/// </summary>
internal sealed class FixtureHost(Func<float2, float> height) : ILayoutHost {
  public float HeightAt(float2 xz) => height(xz);

  public void Fill(ToeDistanceJob job) {
    for (var i = 0; i < job.distances.Length; i++) {
      job.Execute(i);
    }
  }
}
