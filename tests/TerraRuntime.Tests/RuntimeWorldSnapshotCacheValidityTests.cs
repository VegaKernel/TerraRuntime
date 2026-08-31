using System.Buffers.Binary;
using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldSnapshotCacheValidityTests
{
    [Fact]
    public async Task Validated_load_accepts_matching_canonical_fingerprint()
    {
        CacheFixture fixture = await CacheFixture.CreateAsync(TestContext.Current.CancellationToken);
        try
        {
            RuntimeWorldSnapshotLoadDiagnostic load = RuntimeWorldSnapshotCache.TryLoadValidatedSource(
                fixture.CachePath,
                fixture.WorldPath,
                fixture.Limits,
                out WorldFileData? cachedWorld);

            Assert.True(load.IsLoaded);
            Assert.NotNull(cachedWorld);
            Assert.Equal(fixture.World.Header.WorldId, cachedWorld!.Header.WorldId);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Same_length_source_mutation_with_restored_mtime_is_rejected()
    {
        CacheFixture fixture = await CacheFixture.CreateAsync(TestContext.Current.CancellationToken);
        try
        {
            byte[] changed = fixture.SourceFile.ToArray();
            changed[^1] ^= 0x5A;
            await File.WriteAllBytesAsync(
                fixture.WorldPath,
                changed,
                TestContext.Current.CancellationToken);
            File.SetLastWriteTimeUtc(
                fixture.WorldPath,
                new DateTime(fixture.SourceStamp.LastWriteTimeUtcTicks, DateTimeKind.Utc));

            RuntimeWorldSnapshotLoadDiagnostic load = RuntimeWorldSnapshotCache.TryLoadValidatedSource(
                fixture.CachePath,
                fixture.WorldPath,
                fixture.Limits,
                out WorldFileData? cachedWorld);

            Assert.Equal(RuntimeWorldSnapshotLoadResult.SourceFingerprintMismatch, load.Result);
            Assert.Null(cachedWorld);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Theory]
    [InlineData(48, RuntimeWorldSnapshotLoadResult.SchemaVersionMismatch)]
    [InlineData(52, RuntimeWorldSnapshotLoadResult.LayoutVersionMismatch)]
    public async Task Runtime_contract_version_mismatch_is_machine_readable(
        int offset,
        RuntimeWorldSnapshotLoadResult expected)
    {
        CacheFixture fixture = await CacheFixture.CreateAsync(TestContext.Current.CancellationToken);
        try
        {
            PatchInt32(fixture.CachePath, offset, int.MaxValue);

            RuntimeWorldSnapshotLoadDiagnostic load = RuntimeWorldSnapshotCache.TryLoadValidatedSource(
                fixture.CachePath,
                fixture.WorldPath,
                fixture.Limits,
                out WorldFileData? cachedWorld);

            Assert.Equal(expected, load.Result);
            Assert.Null(cachedWorld);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task World_format_version_mismatch_is_machine_readable()
    {
        CacheFixture fixture = await CacheFixture.CreateAsync(TestContext.Current.CancellationToken);
        try
        {
            PatchInt32(fixture.CachePath, 72, fixture.World.Envelope.FormatVersion + 1);

            RuntimeWorldSnapshotLoadDiagnostic load = RuntimeWorldSnapshotCache.TryLoadValidatedSource(
                fixture.CachePath,
                fixture.WorldPath,
                fixture.Limits,
                out WorldFileData? cachedWorld);

            Assert.Equal(RuntimeWorldSnapshotLoadResult.WorldFormatMismatch, load.Result);
            Assert.Null(cachedWorld);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Tile_shard_corruption_is_detected_after_source_fingerprint_acceptance()
    {
        CacheFixture fixture = await CacheFixture.CreateAsync(TestContext.Current.CancellationToken);
        try
        {
            long firstTileByte = 128L + fixture.SourceFile.LongLength;
            using (var stream = new FileStream(fixture.CachePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                stream.Position = firstTileByte;
                int original = stream.ReadByte();
                Assert.True(original >= 0);
                stream.Position = firstTileByte;
                stream.WriteByte((byte)(original ^ 0x01));
                stream.Flush(flushToDisk: true);
            }

            RuntimeWorldSnapshotLoadDiagnostic load = RuntimeWorldSnapshotCache.TryLoadValidatedSource(
                fixture.CachePath,
                fixture.WorldPath,
                fixture.Limits,
                out WorldFileData? cachedWorld);

            Assert.Equal(RuntimeWorldSnapshotLoadResult.PayloadHashMismatch, load.Result);
            Assert.Null(cachedWorld);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static void PatchInt32(string path, long offset, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = offset;
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static T LoaderFixture<T>(string methodName)
    {
        MethodInfo? method = typeof(WorldFileLoaderTests).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, null));
    }

    private sealed class CacheFixture : IDisposable
    {
        private CacheFixture(
            string directory,
            string worldPath,
            string cachePath,
            byte[] sourceFile,
            WorldFileLoadLimits limits,
            WorldFileData world,
            RuntimeWorldSourceStamp sourceStamp)
        {
            Directory = directory;
            WorldPath = worldPath;
            CachePath = cachePath;
            SourceFile = sourceFile;
            Limits = limits;
            World = world;
            SourceStamp = sourceStamp;
        }

        public string Directory { get; }
        public string WorldPath { get; }
        public string CachePath { get; }
        public byte[] SourceFile { get; }
        public WorldFileLoadLimits Limits { get; }
        public WorldFileData World { get; }
        public RuntimeWorldSourceStamp SourceStamp { get; }

        public static async Task<CacheFixture> CreateAsync(CancellationToken cancellationToken)
        {
            byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
            WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
            Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? loadedWorld).IsLoaded);
            WorldFileData world = Assert.IsType<WorldFileData>(loadedWorld);

            string directory = Path.Combine(
                Path.GetTempPath(),
                $"TerraRuntime-CacheValidity-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            string worldPath = Path.Combine(directory, "world.wld");
            string cachePath = RuntimeWorldSnapshotCache.GetCachePath(worldPath);
            await File.WriteAllBytesAsync(worldPath, sourceFile, cancellationToken);
            Assert.True(RuntimeWorldSnapshotCache.TryCaptureSourceStamp(
                worldPath,
                out RuntimeWorldSourceStamp sourceStamp));
            RuntimeWorldSnapshotWriteDiagnostic write = RuntimeWorldSnapshotCache.TryWriteAtomic(
                cachePath,
                sourceFile,
                sourceStamp,
                world);
            Assert.True(write.IsWritten);

            return new CacheFixture(
                directory,
                worldPath,
                cachePath,
                sourceFile,
                limits,
                world,
                sourceStamp);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
