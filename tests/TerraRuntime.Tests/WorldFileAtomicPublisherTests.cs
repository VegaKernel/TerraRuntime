using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileAtomicPublisherTests
{
    [Fact]
    public void TryCreate_publishes_complete_payload_and_refuses_overwrite()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TerraRuntime.Tests", Guid.NewGuid().ToString("N"));
        string worldPath = Path.Combine(directory, "generated.wld");
        byte[] first = [1, 2, 3, 4, 5];
        byte[] second = [9, 9, 9];

        try
        {
            WorldFileAtomicPublishDiagnostic created = WorldFileAtomicPublisher.TryCreate(worldPath, first);
            WorldFileAtomicPublishDiagnostic duplicate = WorldFileAtomicPublisher.TryCreate(worldPath, second);

            Assert.True(created.IsPublished, created.ToString());
            Assert.Equal(WorldFileAtomicPublishResult.AlreadyExists, duplicate.Result);
            Assert.Equal(first, File.ReadAllBytes(worldPath));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryCreate_rejects_empty_payload_without_creating_destination()
    {
        string directory = Path.Combine(Path.GetTempPath(), "TerraRuntime.Tests", Guid.NewGuid().ToString("N"));
        string worldPath = Path.Combine(directory, "generated.wld");

        try
        {
            WorldFileAtomicPublishDiagnostic result = WorldFileAtomicPublisher.TryCreate(worldPath, ReadOnlySpan<byte>.Empty);

            Assert.Equal(WorldFileAtomicPublishResult.InvalidPayload, result.Result);
            Assert.False(File.Exists(worldPath));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
