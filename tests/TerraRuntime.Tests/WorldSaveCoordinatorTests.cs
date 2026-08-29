using System.Text;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class WorldSaveCoordinatorTests
{
    [Fact]
    public async Task Request_captures_before_return_and_serializes_after_handoff()
    {
        string directory = CreateTempDirectory();
        string destination = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var serializationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSerialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int authoritativeValue = 7;
        int captureCount = 0;

        try
        {
            await using var coordinator = new WorldSaveCoordinator<int>(
                destination,
                () =>
                {
                    captureCount++;
                    return authoritativeValue;
                },
                async (snapshot, stream, token) =>
                {
                    serializationStarted.TrySetResult();
                    await releaseSerialization.Task.WaitAsync(token);
                    byte[] bytes = Encoding.UTF8.GetBytes(snapshot.ToString());
                    await stream.WriteAsync(bytes, token);
                });

            coordinator.RequestSave();
            authoritativeValue = 99;

            Assert.Equal(1, captureCount);
            await serializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            Assert.False(File.Exists(destination));

            releaseSerialization.TrySetResult();
            await coordinator.CompleteAsync(cancellationToken);

            Assert.Equal("7", await File.ReadAllTextAsync(destination, cancellationToken));
        }
        finally
        {
            releaseSerialization.TrySetResult();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Active_write_coalesces_captured_snapshots_to_newest_state()
    {
        string directory = CreateTempDirectory();
        string destination = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var firstSerializationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstSerialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serialized = new List<int>();
        int authoritativeValue = 1;

        try
        {
            await using var coordinator = new WorldSaveCoordinator<int>(
                destination,
                () => authoritativeValue,
                async (snapshot, stream, token) =>
                {
                    serialized.Add(snapshot);
                    if (snapshot == 1)
                    {
                        firstSerializationStarted.TrySetResult();
                        await releaseFirstSerialization.Task.WaitAsync(token);
                    }

                    byte[] bytes = Encoding.UTF8.GetBytes(snapshot.ToString());
                    await stream.WriteAsync(bytes, token);
                });

            coordinator.RequestSave();
            await firstSerializationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

            authoritativeValue = 2;
            coordinator.RequestSave();
            authoritativeValue = 3;
            coordinator.RequestSave();
            authoritativeValue = 4;
            coordinator.RequestSave();

            releaseFirstSerialization.TrySetResult();
            await coordinator.CompleteAsync(cancellationToken);

            Assert.Equal(new[] { 1, 4 }, serialized);
            Assert.Equal("4", await File.ReadAllTextAsync(destination, cancellationToken));
        }
        finally
        {
            releaseFirstSerialization.TrySetResult();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Complete_closes_capture_boundary_before_late_requests()
    {
        string directory = CreateTempDirectory();
        string destination = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        int captureCount = 0;

        try
        {
            var coordinator = new WorldSaveCoordinator<int>(
                destination,
                () =>
                {
                    captureCount++;
                    return captureCount;
                },
                static (_, _, _) => Task.CompletedTask);

            await coordinator.CompleteAsync(cancellationToken);

            Assert.Throws<InvalidOperationException>(coordinator.RequestSave);
            Assert.Equal(0, captureCount);
            await coordinator.DisposeAsync();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TerraRuntime-WorldSave-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
