using System.Text;
using TerraRuntime.Core;

if (args.Length < 3)
{
    Console.Error.WriteLine("usage: <write|stall> <destination> <payload> [ready-file]");
    return 2;
}

string mode = args[0];
string destination = Path.GetFullPath(args[1]);
byte[] payload = Encoding.UTF8.GetBytes(args[2]);

switch (mode)
{
    case "write":
        await AtomicSaveFileWriter.WriteAsync(
            destination,
            async (stream, cancellationToken) =>
            {
                await stream.WriteAsync(payload, cancellationToken);
            });
        Console.WriteLine($"atomic_save_write_ok destination={destination} bytes={payload.Length}");
        return 0;

    case "stall":
        if (args.Length != 4)
        {
            Console.Error.WriteLine("stall mode requires a ready-file argument");
            return 2;
        }

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

    default:
        Console.Error.WriteLine($"unknown mode: {mode}");
        return 2;
}
