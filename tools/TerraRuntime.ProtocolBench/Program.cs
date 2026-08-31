using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using global::Multiplicity.Packets;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.ProtocolBench;

internal static class Program
{
    private const int DefaultIterations = 200_000;
    private const int DefaultSamples = 5;
    private const int WarmupIterations = 50_000;
    private const double MaximumThroughputRegressionRatio = 1.50;

    private static readonly byte[] FramePayload = CreateFramePayload();

    private static readonly TerrariaPlayerEquipmentState EquipmentState = new(
        PlayerId: 7,
        SlotId: 12,
        Stack: 99,
        Prefix: 3,
        ItemNetId: 8,
        ItemFlags: 1);

    private static readonly TerrariaPlayerMovementState MovementState = new(
        PlayerId: 7,
        ControlFlags: 0,
        MovementFlags: 0,
        MiscFlags1: 0,
        MiscFlags2: 0,
        SelectedItem: 4,
        PositionX: 123.5f,
        PositionY: 456.25f,
        HasVelocity: false,
        VelocityX: 0,
        VelocityY: 0,
        HasMount: false,
        MountType: 0,
        HasPotionOfReturnPositions: false,
        PotionOfReturnOriginalPositionX: 0,
        PotionOfReturnOriginalPositionY: 0,
        PotionOfReturnHomePositionX: 0,
        PotionOfReturnHomePositionY: 0,
        HasCameraTarget: false,
        CameraTargetX: 0,
        CameraTargetY: 0);

    public static int Main(string[] args)
    {
        BenchmarkOptions options;
        try
        {
            options = BenchmarkOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }

        BenchmarkCase[] cases =
        [
            new("frame-32-byte", CurrentFrame, LegacyFrame),
            new("packet14-player-active", CurrentPlayerActive, LegacyPlayerActive),
            new("packet5-equipment", CurrentEquipment, LegacyEquipment),
            new("packet13-movement-minimal", CurrentMovement, LegacyMovement)
        ];

        var results = new List<BenchmarkCaseResult>(cases.Length);
        bool gatePassed = true;

        foreach (BenchmarkCase benchmarkCase in cases)
        {
            byte[] currentBytes = benchmarkCase.Current();
            byte[] legacyBytes = benchmarkCase.Legacy();
            if (!currentBytes.AsSpan().SequenceEqual(legacyBytes))
            {
                Console.Error.WriteLine($"Wire mismatch for {benchmarkCase.Name}.");
                return 3;
            }

            BenchmarkPairMeasurement pair = MeasurePair(
                benchmarkCase.Current,
                benchmarkCase.Legacy,
                options.Iterations,
                options.Samples);
            BenchmarkMeasurement current = pair.Current;
            BenchmarkMeasurement legacy = pair.Legacy;

            double allocationRatio = legacy.BytesPerOperation == 0
                ? 0
                : current.BytesPerOperation / legacy.BytesPerOperation;
            double timeRatio = legacy.NanosecondsPerOperation == 0
                ? 0
                : current.NanosecondsPerOperation / legacy.NanosecondsPerOperation;

            bool allocationPassed = current.BytesPerOperation < legacy.BytesPerOperation;
            bool throughputPassed = current.NanosecondsPerOperation <=
                legacy.NanosecondsPerOperation * MaximumThroughputRegressionRatio;
            bool casePassed = allocationPassed && throughputPassed;
            gatePassed &= casePassed;

            results.Add(new BenchmarkCaseResult(
                benchmarkCase.Name,
                currentBytes.Length,
                current,
                legacy,
                allocationRatio,
                timeRatio,
                allocationPassed,
                throughputPassed,
                casePassed));

            Console.WriteLine(
                $"{benchmarkCase.Name}: frame={currentBytes.Length} B, " +
                $"current={current.BytesPerOperation:F1} B/op {current.NanosecondsPerOperation:F1} ns/op, " +
                $"legacy={legacy.BytesPerOperation:F1} B/op {legacy.NanosecondsPerOperation:F1} ns/op, " +
                $"allocRatio={allocationRatio:F3}, timeRatio={timeRatio:F3}, gate={(casePassed ? "pass" : "FAIL")}");
        }

        var report = new BenchmarkReport(
            SchemaVersion: 2,
            CreatedUtc: DateTimeOffset.UtcNow,
            CommitSha: Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local",
            Runtime: RuntimeInformation.FrameworkDescription,
            Os: RuntimeInformation.OSDescription,
            Architecture: RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount: Environment.ProcessorCount,
            Iterations: options.Iterations,
            Samples: options.Samples,
            WarmupIterationsPerImplementation: WarmupIterations,
            AlternatingSampleOrder: true,
            MaximumThroughputRegressionRatio: MaximumThroughputRegressionRatio,
            GatePassed: gatePassed,
            Cases: results);

        if (options.JsonPath is not null)
        {
            string? directory = Path.GetDirectoryName(options.JsonPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(
                options.JsonPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }

        if (options.Gate && !gatePassed)
        {
            Console.Error.WriteLine(
                "Protocol hot-path performance gate failed. Current code must allocate less than the preserved legacy path " +
                $"and must not exceed {MaximumThroughputRegressionRatio:F2}x its median ns/op.");
            return 1;
        }

        return 0;
    }

    private static BenchmarkPairMeasurement MeasurePair(
        Func<byte[]> current,
        Func<byte[]> legacy,
        int iterations,
        int samples)
    {
        // Warm both implementations before either is measured. Multiplicity packet serializers share ToStream
        // methods, so measuring the current path first and the legacy path second can otherwise hand tiered-PGO
        // optimization to the second implementation and manufacture a throughput regression that is not real.
        Warm(current);
        Warm(legacy);
        Warm(current);
        Warm(legacy);

        var currentNanoseconds = new double[samples];
        var currentBytes = new double[samples];
        var legacyNanoseconds = new double[samples];
        var legacyBytes = new double[samples];

        for (int sample = 0; sample < samples; sample++)
        {
            // Alternate order to cancel runner temperature, CPU-frequency and cache-order bias across the median.
            if ((sample & 1) == 0)
            {
                MeasureSample(current, iterations, out currentNanoseconds[sample], out currentBytes[sample]);
                MeasureSample(legacy, iterations, out legacyNanoseconds[sample], out legacyBytes[sample]);
            }
            else
            {
                MeasureSample(legacy, iterations, out legacyNanoseconds[sample], out legacyBytes[sample]);
                MeasureSample(current, iterations, out currentNanoseconds[sample], out currentBytes[sample]);
            }
        }

        return new BenchmarkPairMeasurement(
            ToMeasurement(currentNanoseconds, currentBytes),
            ToMeasurement(legacyNanoseconds, legacyBytes));
    }

    private static void Warm(Func<byte[]> operation)
    {
        long checksum = 0;
        for (int iteration = 0; iteration < WarmupIterations; iteration++)
            checksum += operation().Length;
        GC.KeepAlive(checksum);
    }

    private static void MeasureSample(
        Func<byte[]> operation,
        int iterations,
        out double nanosecondsPerOperation,
        out double bytesPerOperation)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long checksum = 0;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long timestampBefore = Stopwatch.GetTimestamp();

        for (int iteration = 0; iteration < iterations; iteration++)
            checksum += operation().Length;

        long timestampAfter = Stopwatch.GetTimestamp();
        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(checksum);

        nanosecondsPerOperation =
            Stopwatch.GetElapsedTime(timestampBefore, timestampAfter).TotalNanoseconds / iterations;
        bytesPerOperation = (allocatedAfter - allocatedBefore) / (double)iterations;
    }

    private static BenchmarkMeasurement ToMeasurement(double[] nanoseconds, double[] bytes)
    {
        Array.Sort(nanoseconds);
        Array.Sort(bytes);
        int medianIndex = nanoseconds.Length / 2;
        double medianNanoseconds = nanoseconds[medianIndex];

        return new BenchmarkMeasurement(
            BytesPerOperation: bytes[medianIndex],
            NanosecondsPerOperation: medianNanoseconds,
            OperationsPerSecond: 1_000_000_000d / medianNanoseconds);
    }

    private static byte[] CurrentFrame()
    {
        byte[] frame = GC.AllocateUninitializedArray<byte>(
            FramePayload.Length + TerrariaFrameDecoderOptions.MinimumFrameLength);
        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            frame.AsSpan(),
            (byte)TerrariaMessageId.PlayerControls,
            FramePayload);
        if (result != TerrariaFrameWriteResult.Written)
            throw new InvalidOperationException($"Current frame encoder returned {result}.");

        return frame;
    }

    private static byte[] LegacyFrame()
    {
        var writer = new ArrayBufferWriter<byte>(
            FramePayload.Length + TerrariaFrameDecoderOptions.MinimumFrameLength);
        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)TerrariaMessageId.PlayerControls,
            FramePayload);
        if (result != TerrariaFrameWriteResult.Written)
            throw new InvalidOperationException($"Legacy frame encoder returned {result}.");

        return writer.WrittenSpan.ToArray();
    }

    private static byte[] CurrentPlayerActive() => TerrariaPlayerActiveEncoder.Encode(playerId: 7, active: true);

    private static byte[] LegacyPlayerActive() => LegacySerialize(new PlayerActive
    {
        PlayerId = 7,
        Active = true
    });

    private static byte[] CurrentEquipment() => TerrariaPlayerEquipmentCodec.Encode(EquipmentState);

    private static byte[] LegacyEquipment() => LegacySerialize(new PlayerSlot
    {
        PlayerId = EquipmentState.PlayerId,
        SlotId = EquipmentState.SlotId,
        Stack = EquipmentState.Stack,
        Prefix = EquipmentState.Prefix,
        ItemNetId = EquipmentState.ItemNetId,
        ItemFlags = EquipmentState.ItemFlags
    });

    private static byte[] CurrentMovement() => TerrariaPlayerMovementEncoder.Encode(MovementState);

    private static byte[] LegacyMovement() => LegacySerialize(new PlayerUpdate
    {
        PlayerId = MovementState.PlayerId,
        ControlFlags = (UpdatePlayerControlFlags)MovementState.ControlFlags,
        MovementFlags = (UpdatePlayerMovementFlags)MovementState.MovementFlags,
        MiscFlags1 = (UpdatePlayerMiscFlags1)MovementState.MiscFlags1,
        MiscFlags2 = (UpdatePlayerMiscFlags2)MovementState.MiscFlags2,
        SelectedItem = MovementState.SelectedItem,
        PositionX = MovementState.PositionX,
        PositionY = MovementState.PositionY,
        VelocityX = MovementState.VelocityX,
        VelocityY = MovementState.VelocityY,
        MountType = MovementState.MountType,
        PotionOfReturnOriginalPositionX = MovementState.PotionOfReturnOriginalPositionX,
        PotionOfReturnOriginalPositionY = MovementState.PotionOfReturnOriginalPositionY,
        PotionOfReturnHomePositionX = MovementState.PotionOfReturnHomePositionX,
        PotionOfReturnHomePositionY = MovementState.PotionOfReturnHomePositionY,
        CameraTargetX = MovementState.CameraTargetX,
        CameraTargetY = MovementState.CameraTargetY
    });

    private static byte[] LegacySerialize(TerrariaPacket packet)
    {
        int frameLength = checked(packet.GetLength() + TerrariaPacket.PacketHeaderLength);
        var writer = new ArrayBufferWriter<byte>(frameLength);
        using var stream = new LegacyBufferWriterStream(writer);
        packet.ToStream(stream);
        if (writer.WrittenCount != frameLength)
        {
            throw new InvalidOperationException(
                $"Legacy serializer wrote {writer.WrittenCount} bytes for declared frame length {frameLength}.");
        }

        return writer.WrittenSpan.ToArray();
    }

    private static byte[] CreateFramePayload()
    {
        var payload = new byte[32];
        for (int index = 0; index < payload.Length; index++)
            payload[index] = (byte)(index * 7 + 3);
        return payload;
    }

    private sealed class LegacyBufferWriterStream : Stream
    {
        private readonly IBufferWriter<byte> writer;
        private long length;

        public LegacyBufferWriterStream(IBufferWriter<byte> writer) => this.writer = writer;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => length;

        public override long Position
        {
            get => length;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.IsEmpty)
                return;

            Span<byte> destination = writer.GetSpan(buffer.Length);
            buffer.CopyTo(destination);
            writer.Advance(buffer.Length);
            length += buffer.Length;
        }

        public override void WriteByte(byte value)
        {
            Span<byte> destination = writer.GetSpan(1);
            destination[0] = value;
            writer.Advance(1);
            length++;
        }
    }

    private sealed record BenchmarkCase(string Name, Func<byte[]> Current, Func<byte[]> Legacy);

    private sealed record BenchmarkPairMeasurement(
        BenchmarkMeasurement Current,
        BenchmarkMeasurement Legacy);

    private sealed record BenchmarkMeasurement(
        double BytesPerOperation,
        double NanosecondsPerOperation,
        double OperationsPerSecond);

    private sealed record BenchmarkCaseResult(
        string Name,
        int FrameBytes,
        BenchmarkMeasurement Current,
        BenchmarkMeasurement Legacy,
        double AllocationRatio,
        double TimeRatio,
        bool AllocationPassed,
        bool ThroughputPassed,
        bool GatePassed);

    private sealed record BenchmarkReport(
        int SchemaVersion,
        DateTimeOffset CreatedUtc,
        string CommitSha,
        string Runtime,
        string Os,
        string Architecture,
        int ProcessorCount,
        int Iterations,
        int Samples,
        int WarmupIterationsPerImplementation,
        bool AlternatingSampleOrder,
        double MaximumThroughputRegressionRatio,
        bool GatePassed,
        IReadOnlyList<BenchmarkCaseResult> Cases);

    private sealed record BenchmarkOptions(int Iterations, int Samples, bool Gate, string? JsonPath)
    {
        public static BenchmarkOptions Parse(string[] args)
        {
            int iterations = DefaultIterations;
            int samples = DefaultSamples;
            bool gate = false;
            string? jsonPath = null;

            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--iterations":
                        iterations = ParsePositiveInteger(args, ref index, "--iterations");
                        break;
                    case "--samples":
                        samples = ParsePositiveInteger(args, ref index, "--samples");
                        if ((samples & 1) == 0)
                            throw new ArgumentException("--samples must be odd so the median is unambiguous.");
                        break;
                    case "--gate":
                        gate = true;
                        break;
                    case "--json":
                        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                            throw new ArgumentException("--json requires a path.");
                        jsonPath = args[index];
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument: {args[index]}");
                }
            }

            return new BenchmarkOptions(iterations, samples, gate, jsonPath);
        }

        private static int ParsePositiveInteger(string[] args, ref int index, string option)
        {
            if (++index >= args.Length || !int.TryParse(args[index], out int value) || value <= 0)
                throw new ArgumentException($"{option} requires a positive integer.");
            return value;
        }
    }
}
