using System.Text;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class AtomicSaveFileWriterTests
{
    [Fact]
    public async Task Successful_write_replaces_existing_destination()
    {
        string directory = CreateTempDirectory();
        string destination = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await File.WriteAllTextAsync(destination, "old", cancellationToken);

            await AtomicSaveFileWriter.WriteAsync(
                destination,
                async (stream, token) =>
                {
                    byte[] payload = Encoding.UTF8.GetBytes("new");
                    await stream.WriteAsync(payload, token);
                },
                cancellationToken);

            Assert.Equal("new", await File.ReadAllTextAsync(destination, cancellationToken));
            Assert.Empty(Directory.EnumerateFiles(directory, ".world.wld.*.tmp"));
            Assert.Empty(Directory.EnumerateFiles(directory, ".world.wld.*.tmp.lease"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Failed_write_preserves_existing_destination_and_removes_temp_file()
    {
        string directory = CreateTempDirectory();
        string destination = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var expected = new IOException("serialization failed");

        try
        {
            await File.WriteAllTextAsync(destination, "old", cancellationToken);

            IOException observed = await Assert.ThrowsAsync<IOException>(() =>
                AtomicSaveFileWriter.WriteAsync(
                    destination,
                    async (stream, token) =>
                    {
                        byte[] partial = Encoding.UTF8.GetBytes("partial");
                        await stream.WriteAsync(partial, token);
                        throw expected;
                    },
                    cancellationToken));

            Assert.Same(expected, observed);
            Assert.Equal("old", await File.ReadAllTextAsync(destination, cancellationToken));
            Assert.Empty(Directory.EnumerateFiles(directory, ".world.wld.*.tmp"));
            Assert.Empty(Directory.EnumerateFiles(directory, ".world.wld.*.tmp.lease"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task First_write_creates_destination_only_after_writer_completes()
    {
        string directory = CreateTempDirectory();
        string destination = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var writerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWriter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Task write = AtomicSaveFileWriter.WriteAsync(
                destination,
                async (stream, token) =>
                {
                    writerStarted.TrySetResult();
                    await releaseWriter.Task.WaitAsync(token);
                    byte[] payload = Encoding.UTF8.GetBytes("complete");
                    await stream.WriteAsync(payload, token);
                },
                cancellationToken);

            await writerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            Assert.False(File.Exists(destination));

            releaseWriter.TrySetResult();
            await write;

            Assert.Equal("complete", await File.ReadAllTextAsync(destination, cancellationToken));
        }
        finally
        {
            releaseWriter.TrySetResult();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Next_write_reaps_abandoned_temp_when_its_lease_is_not_owned()
    {
        string directory = CreateTempDirectory();
        string destination = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string token = Guid.NewGuid().ToString("N");
        string abandonedTemp = Path.Combine(directory, $".world.wld.{token}.tmp");
        string abandonedLease = abandonedTemp + ".lease";

        try
        {
            await File.WriteAllTextAsync(abandonedTemp, "abandoned", cancellationToken);
            await File.WriteAllTextAsync(abandonedLease, "lease", cancellationToken);

            await AtomicSaveFileWriter.WriteAsync(
                destination,
                async (stream, tokenValue) =>
                {
                    byte[] payload = Encoding.UTF8.GetBytes("committed");
                    await stream.WriteAsync(payload, tokenValue);
                },
                cancellationToken);

            Assert.False(File.Exists(abandonedTemp));
            Assert.False(File.Exists(abandonedLease));
            Assert.Equal("committed", await File.ReadAllTextAsync(destination, cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Cleanup_never_deletes_temp_owned_by_a_live_lease()
    {
        string directory = CreateTempDirectory();
        string destination = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string token = Guid.NewGuid().ToString("N");
        string liveTemp = Path.Combine(directory, $".world.wld.{token}.tmp");
        string liveLease = liveTemp + ".lease";

        try
        {
            await File.WriteAllTextAsync(liveTemp, "live", cancellationToken);
            await using (var lease = new FileStream(
                liveLease,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    BufferSize = 1,
                    Options = FileOptions.Asynchronous
                }))
            {
                await AtomicSaveFileWriter.WriteAsync(
                    destination,
                    async (stream, tokenValue) =>
                    {
                        byte[] payload = Encoding.UTF8.GetBytes("first");
                        await stream.WriteAsync(payload, tokenValue);
                    },
                    cancellationToken);

                Assert.True(File.Exists(liveTemp));
                Assert.True(File.Exists(liveLease));
            }

            await AtomicSaveFileWriter.WriteAsync(
                destination,
                async (stream, tokenValue) =>
                {
                    byte[] payload = Encoding.UTF8.GetBytes("second");
                    await stream.WriteAsync(payload, tokenValue);
                },
                cancellationToken);

            Assert.False(File.Exists(liveTemp));
            Assert.False(File.Exists(liveLease));
            Assert.Equal("second", await File.ReadAllTextAsync(destination, cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Legacy_temp_without_lease_is_left_untouched()
    {
        string directory = CreateTempDirectory();
        string destination = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string legacyTemp = Path.Combine(directory, $".world.wld.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(legacyTemp, "unknown-owner", cancellationToken);

            await AtomicSaveFileWriter.WriteAsync(
                destination,
                async (stream, tokenValue) =>
                {
                    byte[] payload = Encoding.UTF8.GetBytes("committed");
                    await stream.WriteAsync(payload, tokenValue);
                },
                cancellationToken);

            Assert.True(File.Exists(legacyTemp));
            Assert.Equal("unknown-owner", await File.ReadAllTextAsync(legacyTemp, cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TerraRuntime-Save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
