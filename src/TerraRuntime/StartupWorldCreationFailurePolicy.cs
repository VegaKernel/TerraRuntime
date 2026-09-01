namespace TerraRuntime;

internal static class StartupWorldCreationFailurePolicy
{
    private const string LogFileName = "world-generation-errors.log";

    internal static bool IsRecoverable(Exception exception) =>
        exception is not OutOfMemoryException;

    internal static Exception? ExtractException(in RuntimeWorldCreationPersistenceResult result) =>
        result.Creation?.Generation.Execution?.Error;

    internal static string? TryWriteDiagnostic(
        string logsDirectory,
        in StartupWorldCreationRequest request,
        RuntimeWorldCreationPersistenceStatus? status,
        Exception? exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);

        try
        {
            Directory.CreateDirectory(logsDirectory);
            string path = Path.Combine(logsDirectory, LogFileName);
            string diagnostic = BuildDiagnostic(in request, status, exception);
            File.AppendAllText(path, diagnostic);
            return Path.GetFullPath(path);
        }
        catch (Exception loggingException) when (IsRecoverable(loggingException))
        {
            return null;
        }
    }

    internal static bool PromptReturnToWorldSelection(TextReader input, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        while (true)
        {
            output.WriteLine();
            output.Write("Generation failed. Press Enter or B to return to world selection, or Q to quit: ");
            string? value = input.ReadLine();
            if (value is null)
                return false;

            value = value.Trim();
            if (value.Length == 0 ||
                string.Equals(value, "b", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "back", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(value, "q", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "quit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "exit", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            output.WriteLine("Choose Enter/B for world selection or Q to quit.");
        }
    }

    private static string BuildDiagnostic(
        in StartupWorldCreationRequest request,
        RuntimeWorldCreationPersistenceStatus? status,
        Exception? exception)
    {
        string line = new('=', 80);
        return string.Join(
            Environment.NewLine,
            line,
            $"UTC: {DateTimeOffset.UtcNow:O}",
            $"World: {request.Generation.WorldName}",
            $"Generator: {request.Generation.GeneratorId.Value}",
            $"Seed: {request.Generation.Seed}",
            $"Size: {request.Generation.WidthTiles}x{request.Generation.HeightTiles}",
            $"GameMode: {request.Generation.Options.GameMode}",
            $"Evil: {request.Generation.Options.Evil}",
            $"Output: {request.OutputPath}",
            $"Status: {status?.ToString() ?? "UnhandledException"}",
            exception is null ? "Exception: <none>" : exception.ToString(),
            line,
            string.Empty);
    }
}
