namespace TerraRuntime.Core;

/// <summary>
/// Optional publication policy for <see cref="AtomicSaveFileWriter"/>.
/// A checkpoint validator runs against the fully flushed temporary candidate before it can become visible.
/// When <see cref="BackupPath"/> is configured and the destination already exists, the previous destination is
/// copied to a separate atomic temporary file, validated, and durably published as the backup before the new
/// canonical checkpoint replaces it.
/// </summary>
public sealed record AtomicSaveFileWriterOptions(
    string? BackupPath = null,
    Func<string, CancellationToken, Task>? ValidateCheckpointAsync = null);
