using System.Text;
using TerraRuntime.Core;
using TerraRuntime.World;

if (args.Length < 3)
{
    Console.Error.WriteLine("usage: <write|stall|stall-file|stall-recovery-ready-file|stall-after-publish-file|recover|publish|publish-refuse> <destination> <payload-or-source> [ready-file]");
    return 2;
}

string mode = args[0];
string destination = Path.GetFullPath(args[1]);

switch (mode)
{
    case "write":
    {
        byte[] payload = Encoding.UTF8.GetBytes(args[2]);
        await AtomicSaveFileWriter.WriteAsync(
            destination,
            async (stream, cancellationToken) =>
            {
                await stream.WriteAsync(payload, cancellationToken);
            });
        Console.WriteLine($"atomic_save_write_ok destination={destination} bytes={payload.Length}");
        return 0;
    }

    case "stall":
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("stall mode requires a ready-file argument");
            return 2;
        }

        byte[] payload = Encoding.UTF8.GetBytes(args[2]);
        string readyFile = Path.GetFullPath(args[3]);
        await AtomicSaveFileWriter.WriteAsync(
            destination,
            async (stream, cancellationToken) =>
            {
                await stream.WriteAsync(payload, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                Directory.CreateDirectory(Path.GetDirectoryName(readyFile)!);
                await File.WriteAllTextAsync(readyFile, "ready", cancellationToken);
                Console.WriteLine($"atomic_save_stalled destination={destination} bytes={payload.Length}");
                Console.Out.Flush();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });
        return 0;
    }

    case "stall-file":
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("stall-file mode requires a ready-file argument");
            return 2;
        }

        string sourcePath = Path.GetFullPath(args[2]);
        string readyFile = Path.GetFullPath(args[3]);
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists)
        {
            Console.Error.WriteLine($"stall-file source does not exist: {sourcePath}");
            return 2;
        }

        await AtomicSaveFileWriter.WriteAsync(
            destination,
            async (stream, cancellationToken) =>
            {
                await using var source = new FileStream(
                    sourcePath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.Open,
                        Access = FileAccess.Read,
                        Share = FileShare.Read,
                        BufferSize = 64 * 1024,
                        Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                    });
                await source.CopyToAsync(stream, 64 * 1024, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                Directory.CreateDirectory(Path.GetDirectoryName(readyFile)!);
                await File.WriteAllTextAsync(readyFile, "ready", cancellationToken);
                Console.WriteLine($"atomic_save_file_stalled destination={destination} source={sourcePath} bytes={sourceInfo.Length}");
                Console.Out.Flush();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });
        return 0;
    }

    case "stall-recovery-ready-file":
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("stall-recovery-ready-file mode requires a ready-file argument");
            return 2;
        }

        string sourcePath = Path.GetFullPath(args[2]);
        string readyFile = Path.GetFullPath(args[3]);
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists)
        {
            Console.Error.WriteLine($"stall-recovery-ready-file source does not exist: {sourcePath}");
            return 2;
        }

        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        string token = Guid.NewGuid().ToString("N");
        string temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{token}.tmp");
        string leasePath = temporary + ".lease";
        string markerPath = temporary + ".recovery";

        await using var lease = new FileStream(
            leasePath,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                BufferSize = 1,
                Options = FileOptions.WriteThrough
            });

        await using (var source = new FileStream(
            sourcePath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = 64 * 1024,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            }))
        await using (var temporaryStream = new FileStream(
            temporary,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 64 * 1024,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough
            }))
        {
            await source.CopyToAsync(temporaryStream, 64 * 1024);
            await temporaryStream.FlushAsync();
            temporaryStream.Flush(flushToDisk: true);
        }

        await AtomicSaveFileWriter.WriteRecoveryMarkerForTestingAsync(
            markerPath,
            temporary,
            backupPath: null);

        Directory.CreateDirectory(Path.GetDirectoryName(readyFile)!);
        await File.WriteAllTextAsync(readyFile, "ready");
        Console.WriteLine(
            $"atomic_save_recovery_ready_stalled destination={destination} source={sourcePath} bytes={sourceInfo.Length} " +
            $"temporary={temporary} marker={markerPath}");
        Console.Out.Flush();
        await Task.Delay(Timeout.InfiniteTimeSpan);
        return 0;
    }

    case "stall-after-publish-file":
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("stall-after-publish-file mode requires a ready-file argument");
            return 2;
        }

        string sourcePath = Path.GetFullPath(args[2]);
        string readyFile = Path.GetFullPath(args[3]);
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists)
        {
            Console.Error.WriteLine($"stall-after-publish-file source does not exist: {sourcePath}");
            return 2;
        }

        await AtomicSaveFileWriter.WriteAsync(
            destination,
            async (stream, cancellationToken) =>
            {
                await using var source = new FileStream(
                    sourcePath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.Open,
                        Access = FileAccess.Read,
                        Share = FileShare.Read,
                        BufferSize = 64 * 1024,
                        Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                    });
                await source.CopyToAsync(stream, 64 * 1024, cancellationToken);
            },
            options: null,
            afterPublicationAsync: async (publishedPath, cancellationToken) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(readyFile)!);
                await File.WriteAllTextAsync(readyFile, "ready", cancellationToken);
                Console.WriteLine(
                    $"atomic_save_post_publish_stalled destination={publishedPath} source={sourcePath} bytes={sourceInfo.Length}");
                Console.Out.Flush();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            cancellationToken: default);
        return 0;
    }

    case "recover":
    {
        AtomicSaveFileRecoveryDiagnostic recovery = AtomicSaveFileWriter.RecoverAbandonedWrites(destination);
        Console.WriteLine(
            $"atomic_save_recover destination={destination} recovered={recovery.RecoveredWrites} " +
            $"removed={recovery.RemovedWrites} suppressed={recovery.SuppressedWrites} " +
            $"live={recovery.LiveWrites} io_failed={recovery.IoFailed}");
        return recovery.IoFailed || recovery.SuppressedWrites != 0 || recovery.LiveWrites != 0 ? 1 : 0;
    }

    case "publish":
    {
        byte[] payload = Encoding.UTF8.GetBytes(args[2]);
        WorldFileAtomicPublishDiagnostic result = WorldFileAtomicPublisher.TryCreate(destination, payload);
        if (result.Result != WorldFileAtomicPublishResult.Published)
        {
            Console.Error.WriteLine($"atomic_world_publish_failed result={result.Result} destination={destination}");
            return 1;
        }

        Console.WriteLine($"atomic_world_publish_ok destination={destination} bytes={payload.Length}");
        return 0;
    }

    case "publish-refuse":
    {
        byte[] payload = Encoding.UTF8.GetBytes(args[2]);
        WorldFileAtomicPublishDiagnostic result = WorldFileAtomicPublisher.TryCreate(destination, payload);
        if (result.Result != WorldFileAtomicPublishResult.AlreadyExists)
        {
            Console.Error.WriteLine($"atomic_world_refuse_failed result={result.Result} destination={destination}");
            return 1;
        }

        Console.WriteLine($"atomic_world_refuse_ok destination={destination} preserved=true");
        return 0;
    }

    default:
        Console.Error.WriteLine($"unknown mode: {mode}");
        return 2;
}
