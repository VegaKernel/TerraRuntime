namespace TerraRuntime.Application.Operations;

/// <summary>
/// Converts observed process-lifetime outbound queue high-water marks into a conservative sizing recommendation.
/// The configured envelope is the structural correctness floor; measurements may demand more capacity but never
/// shrink below that floor. A 75% target leaves 25% headroom above the observed peak.
/// </summary>
internal static class OutboundQueueSizingEvidenceCalculator
{
    public const int TargetUtilizationPercent = 75;

    public static OutboundQueueSizingEvidence Calculate(
        int structuralMaxFrames,
        long structuralMaxQueuedBytes,
        long peakQueuedFrames,
        long peakQueuedBytes,
        long rejectedFrames,
        int slowClients)
    {
        if (structuralMaxFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(structuralMaxFrames));
        if (structuralMaxQueuedBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(structuralMaxQueuedBytes));
        if (peakQueuedFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(peakQueuedFrames));
        if (peakQueuedBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(peakQueuedBytes));
        if (rejectedFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(rejectedFrames));
        if (slowClients < 0)
            throw new ArgumentOutOfRangeException(nameof(slowClients));

        int measuredFramesWithHeadroom = peakQueuedFrames == 0
            ? 0
            : checked((int)Math.Min(
                int.MaxValue,
                DivideRoundUp(checked(peakQueuedFrames * 100L), TargetUtilizationPercent)));
        long measuredBytesWithHeadroom = peakQueuedBytes == 0
            ? 0
            : DivideRoundUp(checked(peakQueuedBytes * 100L), TargetUtilizationPercent);

        int recommendedMaxFrames = Math.Max(structuralMaxFrames, measuredFramesWithHeadroom);
        long recommendedMaxQueuedBytes = Math.Max(structuralMaxQueuedBytes, measuredBytesWithHeadroom);
        int frameUtilizationBasisPoints = structuralMaxFrames == 0
            ? 0
            : ToBasisPoints(peakQueuedFrames, structuralMaxFrames);
        int byteUtilizationBasisPoints = structuralMaxQueuedBytes == 0
            ? 0
            : ToBasisPoints(peakQueuedBytes, structuralMaxQueuedBytes);

        bool capacityPressure =
            measuredFramesWithHeadroom > structuralMaxFrames ||
            measuredBytesWithHeadroom > structuralMaxQueuedBytes ||
            rejectedFrames != 0 ||
            slowClients != 0;

        return new OutboundQueueSizingEvidence(
            StructuralMaxFrames: structuralMaxFrames,
            StructuralMaxQueuedBytes: structuralMaxQueuedBytes,
            PeakQueuedFrames: peakQueuedFrames,
            PeakQueuedBytes: peakQueuedBytes,
            FrameUtilizationBasisPoints: frameUtilizationBasisPoints,
            ByteUtilizationBasisPoints: byteUtilizationBasisPoints,
            MeasuredFramesWithHeadroom: measuredFramesWithHeadroom,
            MeasuredBytesWithHeadroom: measuredBytesWithHeadroom,
            RecommendedMaxFrames: recommendedMaxFrames,
            RecommendedMaxQueuedBytes: recommendedMaxQueuedBytes,
            RejectedFrames: rejectedFrames,
            SlowClients: slowClients,
            HasMeasurements: peakQueuedFrames != 0 || peakQueuedBytes != 0,
            RequiresReview: capacityPressure);
    }

    private static long DivideRoundUp(long value, long divisor) =>
        checked((value + divisor - 1) / divisor);

    private static int ToBasisPoints(long peak, long capacity)
    {
        if (peak <= 0 || capacity <= 0)
            return 0;

        long basisPoints = DivideRoundUp(checked(peak * 10_000L), capacity);
        return checked((int)Math.Min(int.MaxValue, basisPoints));
    }
}

internal readonly record struct OutboundQueueSizingEvidence(
    int StructuralMaxFrames,
    long StructuralMaxQueuedBytes,
    long PeakQueuedFrames,
    long PeakQueuedBytes,
    int FrameUtilizationBasisPoints,
    int ByteUtilizationBasisPoints,
    int MeasuredFramesWithHeadroom,
    long MeasuredBytesWithHeadroom,
    int RecommendedMaxFrames,
    long RecommendedMaxQueuedBytes,
    long RejectedFrames,
    int SlowClients,
    bool HasMeasurements,
    bool RequiresReview);
