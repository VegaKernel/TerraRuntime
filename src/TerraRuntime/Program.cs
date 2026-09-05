namespace TerraRuntime.Server;

internal static class Program
{
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return global::TerraRuntime.Application.StartupProgram.Main(args);
    }
}
