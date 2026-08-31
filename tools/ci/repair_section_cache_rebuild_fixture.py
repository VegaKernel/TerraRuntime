from pathlib import Path

path = Path("tests/TerraRuntime.Tests/SectionCacheRebuildPipelineTests.cs")
text = path.read_text(encoding="utf-8")
marker = "    private static WorldFileData LoadCompleteWorld() =>"
if marker not in text:
    raise SystemExit("stale SectionCacheRebuildPipelineTests helper marker not found")

start = text.index(marker)
replacement = '''    private static WorldFileData LoadCompleteWorld()
    {
        byte[] source = (byte[])InvokeWorldLoaderTestHelper("CreateCompleteCurrentWorld")!;
        WorldFileLoadLimits limits = (WorldFileLoadLimits)InvokeWorldLoaderTestHelper("CreateLimits")!;
        Assert.True(WorldFileLoader.TryLoad(source, limits, out WorldFileData? loaded).IsLoaded);
        return Assert.IsType<WorldFileData>(loaded);
    }

    private static WorldFileData CreateMultiSectionWorld()
    {
        WorldFileData source = LoadCompleteWorld();
        WorldDimensions dimensions = new(420, 320);
        var tiles = new WorldTileStore(dimensions);
        WorldFileHeader header = source.Header with
        {
            RightWorld = source.Header.LeftWorld + dimensions.WidthTiles * 16,
            BottomWorld = source.Header.TopWorld + dimensions.HeightTiles * 16,
            Dimensions = dimensions
        };

        return source with
        {
            Header = header,
            Tiles = tiles
        };
    }

    private static object? InvokeWorldLoaderTestHelper(string name)
    {
        MethodInfo method = typeof(WorldFileLoaderTests).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"World loader test helper '{name}' was not found.");
        return method.Invoke(null, null);
    }
}
'''
path.write_text(text[:start] + replacement, encoding="utf-8")
