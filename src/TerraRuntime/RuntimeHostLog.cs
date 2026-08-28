using TerraRuntime.Operations;

namespace TerraRuntime;

internal sealed class RuntimeHostLog
{
    private readonly RuntimeLogBuffer runtimeLogs;
    private readonly TextWriter standardOutput;
    private readonly TextWriter standardError;
    private int terminalUiActive;

    public RuntimeHostLog(RuntimeLogBuffer runtimeLogs)
        : this(runtimeLogs, Console.Out, Console.Error)
    {
    }

    internal RuntimeHostLog(
        RuntimeLogBuffer runtimeLogs,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        this.runtimeLogs = runtimeLogs ?? throw new ArgumentNullException(nameof(runtimeLogs));
        this.standardOutput = standardOutput ?? throw new ArgumentNullException(nameof(standardOutput));
        this.standardError = standardError ?? throw new ArgumentNullException(nameof(standardError));
    }

    public bool IsTerminalUiActive => Volatile.Read(ref terminalUiActive) != 0;

    public void SetTerminalUiActive(bool active) =>
        Volatile.Write(ref terminalUiActive, active ? 1 : 0);

    public void Publish(RuntimeLogLevel level, string source, string message) =>
        runtimeLogs.Publish(level, source, message);

    public void Write(
        RuntimeLogLevel level,
        string source,
        string message,
        bool useStandardError = false)
    {
        runtimeLogs.Publish(level, source, message);
        if (IsTerminalUiActive)
            return;

        TextWriter writer = useStandardError ? standardError : standardOutput;
        writer.WriteLine(message);
    }
}
