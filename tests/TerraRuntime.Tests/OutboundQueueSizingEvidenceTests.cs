using TerraRuntime.Application.Operations;

namespace TerraRuntime.Tests;

public sealed class OutboundQueueSizingEvidenceTests
{
    [Fact]
    public void Low_measured_pressure_keeps_structural_floor()
    {
        OutboundQueueSizingEvidence evidence = OutboundQueueSizingEvidenceCalculator.Calculate(
            structuralMaxFrames: 4_077,
            structuralMaxQueuedBytes: 16L * 1024 * 1024,
            peakQueuedFrames: 900,
            peakQueuedBytes: 2L * 1024 * 1024,
            rejectedFrames: 0,
            slowClients: 0);

        Assert.True(evidence.HasMeasurements);
        Assert.False(evidence.RequiresReview);
        Assert.Equal(4_077, evidence.RecommendedMaxFrames);
        Assert.Equal(16L * 1024 * 1024, evidence.RecommendedMaxQueuedBytes);
        Assert.Equal(1_200, evidence.MeasuredFramesWithHeadroom);
        Assert.Equal(2_796_203, evidence.MeasuredBytesWithHeadroom);
        Assert.InRange(evidence.FrameUtilizationBasisPoints, 2_207, 2_208);
        Assert.Equal(1_250, evidence.ByteUtilizationBasisPoints);
    }

    [Fact]
    public void Peak_above_target_expands_recommendation_without_lowering_structural_floor()
    {
        OutboundQueueSizingEvidence evidence = OutboundQueueSizingEvidenceCalculator.Calculate(
            structuralMaxFrames: 100,
            structuralMaxQueuedBytes: 1_000,
            peakQueuedFrames: 90,
            peakQueuedBytes: 900,
            rejectedFrames: 0,
            slowClients: 0);

        Assert.True(evidence.RequiresReview);
        Assert.Equal(120, evidence.MeasuredFramesWithHeadroom);
        Assert.Equal(1_200, evidence.MeasuredBytesWithHeadroom);
        Assert.Equal(120, evidence.RecommendedMaxFrames);
        Assert.Equal(1_200, evidence.RecommendedMaxQueuedBytes);
        Assert.Equal(9_000, evidence.FrameUtilizationBasisPoints);
        Assert.Equal(9_000, evidence.ByteUtilizationBasisPoints);
    }

    [Fact]
    public void Rejections_or_slow_clients_force_review_even_when_peaks_are_low()
    {
        OutboundQueueSizingEvidence evidence = OutboundQueueSizingEvidenceCalculator.Calculate(
            structuralMaxFrames: 100,
            structuralMaxQueuedBytes: 1_000,
            peakQueuedFrames: 10,
            peakQueuedBytes: 100,
            rejectedFrames: 1,
            slowClients: 1);

        Assert.True(evidence.RequiresReview);
        Assert.Equal(100, evidence.RecommendedMaxFrames);
        Assert.Equal(1_000, evidence.RecommendedMaxQueuedBytes);
    }

    [Fact]
    public void Empty_process_has_no_measurement_and_zero_recommendation()
    {
        OutboundQueueSizingEvidence evidence = OutboundQueueSizingEvidenceCalculator.Calculate(
            structuralMaxFrames: 0,
            structuralMaxQueuedBytes: 0,
            peakQueuedFrames: 0,
            peakQueuedBytes: 0,
            rejectedFrames: 0,
            slowClients: 0);

        Assert.False(evidence.HasMeasurements);
        Assert.False(evidence.RequiresReview);
        Assert.Equal(0, evidence.RecommendedMaxFrames);
        Assert.Equal(0, evidence.RecommendedMaxQueuedBytes);
    }
}
