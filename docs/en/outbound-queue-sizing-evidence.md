# Outbound queue sizing evidence

[Русский](../ru/outbound-queue-sizing-evidence.md) · [Networking](networking-protocol.md)

TerraRuntime keeps the configured outbound queue envelope as a structural correctness floor and now exposes process-lifetime measurement evidence for deciding whether that floor needs more headroom.

## Inputs

For every process lifetime the queue telemetry retains:

- configured maximum frames and bytes, even after a connection disconnects;
- peak queued frames and bytes;
- rejected outbound frames;
- slow-client detections.

The configured limits still come from the player-count-aware structural model. Measurements do not silently weaken that bound.

## Measured envelope

The current evidence target is 75% utilization. For an observed peak $P$, the measured envelope with 25% headroom is

$$
M(P)=\left\lceil\frac{P}{0.75}\right\rceil.
$$

Frames and bytes are calculated independently. The safe recommendation is

$$
R_f=\max(S_f,M(P_f)),
$$

$$
R_b=\max(S_b,M(P_b)),
$$

where $S_f$ and $S_b$ are the structural frame and byte floors.

A sizing review is required when either measured envelope exceeds the structural floor, or when queue rejection/slow-client evidence exists even if the peak itself is lower.

## Interpretation

A low measured peak does **not** justify shrinking below the structural join/bootstrap bound. It only demonstrates operational headroom for the observed workload. Raising limits should likewise be based on repeated representative workloads, not one accidental burst.

The runtime network snapshot exposes the structural envelope, utilization in basis points, measured envelopes with headroom, safe recommendations and the `HasMeasurements` / `RequiresReview` decision flags. This makes queue sizing evidence machine-readable without mutating live queue capacity mid-connection.
