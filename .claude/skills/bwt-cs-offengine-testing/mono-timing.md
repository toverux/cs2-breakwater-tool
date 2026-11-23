# Timing managed code

Time it under real Mono: CoreCLR recompiles hot code from its own profile and the game's Mono does not, so a layout as fast as before off-engine can be far slower in the game.

1. Outside the repository, make a `net48` console project (C# 14, `Optimize`, PolySharp) that references the game's `Unity.Mathematics`, `UnityEngine.CoreModule` and `netstandard` from `CSII_MANAGEDPATH` and compiles, from a source root given as a property, `Layout/*.cs`, `BreakwaterParameters.cs` and, from `Scenarios/Ground/`, `FixtureStones`, `ProvingGround`, `ProvingSite` and `ProvingSites`, with `Patches/*.r16` embedded as `Ground.<name>`.
2. Stub the engine's types: `NativeArray` over a managed array, `Allocator`, `ReadOnly`, `WriteOnly`, `IJobParallelFor`, `BurstCompile`, `TerrainSystem`, `TerrainHeightData`, and `TerrainUtils.SampleHeight` copied from the decompile.
3. Time `BreakwaterLayout.Lay` on the sites, its `ILayoutHost.Fill` timed apart and taken off: Burst does it in the game.
4. Build one executable per version compared, the old one from a git worktree, after a `touch` of its sources: MSBuild skips a source moved or copied with its old date.
5. Run the versions in turn over several rounds, the game closed, the CPU clock pinned, and compare medians: `mono -O=-float32 bench.exe` computes a `float` as the game's managed code does, plain `mono` in single precision.
