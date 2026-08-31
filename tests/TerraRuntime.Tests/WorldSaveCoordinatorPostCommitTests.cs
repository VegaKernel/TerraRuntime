using System.Text;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class WorldSaveCoordinatorPostCommitTests
{
    [Fact]
    public async Task Post_commit_callback_observes_published_canonical_file()
    {
        string directory = CreateTempDirectory();
        string destination = Path.Combine(directory, "world.wld");
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        int authoritativeValue = 42;
        int callbackCount = 0;

        try
        {
            await using var coordinator = new WorldSaveCoordinator<int>(
                destination,
                () => authoritativeValue,
                async (snapshot, stream, token) =>
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(snapshot.ToString());
                    await stream.WriteAsync(bytes, token);
                },
                onCommitted: snapshot =>
                {
                    Assert.True(File.Exists(destination));
                    Assert.Equal(snapshot.ToString(), File.ReadAllText(destination));
                    Interlocked.Increment(ref callbackCount);
                });

            coordinator.RequestSave();
            await coordinator.CompleteAsync(cancellationToken);

            Assert.Equal(1, callbackCount);
            Assert.Equal("42", await File.ReadAllTextAsync(destination, cancellationToken));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"TerraRuntime-WorldSavePostCommit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
