using TerraRuntime.Operations;

namespace TerraRuntime;

internal sealed class RuntimeHostLog
{
    private readonly RuntimeLogBuffer runtimeLogs;
    private readonly TextWriter standardOutput;
    private readonly TextWriter standardError;
    private int terminalUiActive;
    private int terminalUiSeen;
    private int plainConsoleActive;

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

    public bool IsPlainConsoleActive => Volatile.Read(ref plainConsoleActive) != 0;

    public void SetTerminalUiActive(bool active)
    {
        if (active)
        {
            Volatile.Write(ref terminalUiSeen, 1);
            Volatile.Write(ref plainConsoleActive, 0);
            Volatile.Write(ref terminalUiActive, 1);
            return;
        }

        Volatile.Write(ref terminalUiActive, 0);
        if (Volatile.Read(ref terminalUiSeen) != 0)
            Volatile.Write(ref plainConsoleActive, 1);
    }

    public void Publish(RuntimeLogLevel level, string source, string message)
    {
        runtimeLogs.Publish(level, source, message);
        if (IsPlainConsoleActive)
            standardOutput.WriteLine(message);
    }

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
