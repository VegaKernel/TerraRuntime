using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class AtomicSaveFileWriterCleanupTests
{
    [Fact]
    public async Task Startup_cleanup_reaps_abandoned_leased_temporary()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string token = Guid.NewGuid().ToString("N");
        string temporary = Path.Combine(directory, $".world.wld.{token}.tmp");
        string lease = temporary + ".lease";

        try
        {
            await File.WriteAllTextAsync(temporary, "abandoned", cancellationToken);
            await File.WriteAllTextAsync(lease, "lease", cancellationToken);

            Assert.True(AtomicSaveFileWriter.TryCleanupAbandonedWrites(target));
            Assert.False(File.Exists(temporary));
            Assert.False(File.Exists(lease));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Startup_cleanup_never_reaps_live_writer_lease()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string token = Guid.NewGuid().ToString("N");
        string temporary = Path.Combine(directory, $".world.wld.{token}.tmp");
        string leasePath = temporary + ".lease";

        try
        {
            await File.WriteAllTextAsync(temporary, "live", cancellationToken);
            await using (var lease = new FileStream(
                leasePath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    BufferSize = 1,
                    Options = FileOptions.Asynchronous
                }))
            {
                Assert.True(AtomicSaveFileWriter.TryCleanupAbandonedWrites(target));
                Assert.True(File.Exists(temporary));
                Assert.True(File.Exists(leasePath));
            }

            Assert.True(AtomicSaveFileWriter.TryCleanupAbandonedWrites(target));
            Assert.False(File.Exists(temporary));
            Assert.False(File.Exists(leasePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Startup_cleanup_leaves_unleased_legacy_temporary_untouched()
    {
        string directory = CreateTempDirectory();
        string target = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string temporary = Path.Combine(directory, $".world.wld.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(temporary, "unknown-owner", cancellationToken);

            Assert.True(AtomicSaveFileWriter.TryCleanupAbandonedWrites(target));
            Assert.True(File.Exists(temporary));
            Assert.Equal("unknown-owner", await File.ReadAllTextAsync(temporary, cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Startup_cleanup_accepts_missing_directory_as_nothing_to_reap()
    {
        string target = Path.Combine(
            Path.GetTempPath(),
            $"TerraRuntime-Missing-{Guid.NewGuid():N}",
            "world.wld");

        Assert.True(AtomicSaveFileWriter.TryCleanupAbandonedWrites(target));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TerraRuntime-Cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
