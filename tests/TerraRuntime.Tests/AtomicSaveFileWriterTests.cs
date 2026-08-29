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
                TestContext.Current.CancellationToken);

            await writerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(File.Exists(destination));

            releaseWriter.TrySetResult();
            await write;

            Assert.Equal("complete", await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
        }
        finally
        {
            releaseWriter.TrySetResult();
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
