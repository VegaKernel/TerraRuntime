namespace TerraRuntime.Tests;

public sealed class RuntimeDirectoryLayoutTests
{
    [Fact]
    public void EnsureCreated_creates_runtime_owned_directories_under_root()
    {
        string root = Path.Combine(Path.GetTempPath(), $"TerraRuntime.Layout.{Guid.NewGuid():N}");

        try
        {
            var layout = new RuntimeDirectoryLayout(root);

            layout.EnsureCreated();

            Assert.Equal(Path.GetFullPath(root), layout.RootDirectory);
            Assert.True(Directory.Exists(layout.WorldsDirectory));
            Assert.True(Directory.Exists(layout.ConfigDirectory));
            Assert.True(Directory.Exists(layout.DataDirectory));
            Assert.True(Directory.Exists(layout.LogsDirectory));
            Assert.Equal(Path.Combine(layout.RootDirectory, "Worlds"), layout.WorldsDirectory);
            Assert.Equal(Path.Combine(layout.RootDirectory, "config"), layout.ConfigDirectory);
            Assert.Equal(Path.Combine(layout.RootDirectory, "data"), layout.DataDirectory);
            Assert.Equal(Path.Combine(layout.RootDirectory, "logs"), layout.LogsDirectory);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
