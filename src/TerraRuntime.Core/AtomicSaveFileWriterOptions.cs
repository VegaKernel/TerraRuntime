namespace TerraRuntime.Core;

/// <summary>
/// Optional publication policy for <see cref="AtomicSaveFileWriter"/>.
/// Validators run against fully flushed temporary files before those files can become visible at their target path.
/// Candidate and backup validation are separate so a caller can fully validate newly serialized state without
/// redundantly re-decoding a previous canonical checkpoint that is already inside its trust chain.
/// </summary>
public sealed record AtomicSaveFileWriterOptions(
    string? BackupPath = null,
    Func<string, CancellationToken, Task>? ValidateCandidateAsync = null,
    Func<string, CancellationToken, Task>? ValidateBackupAsync = null);
