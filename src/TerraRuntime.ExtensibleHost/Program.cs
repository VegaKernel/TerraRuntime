using TerraRuntime.Extensibility;

namespace TerraRuntime.ExtensibleHost;

internal static class Program
{
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return ExtensibleStartup.Main(args);
    }
}
