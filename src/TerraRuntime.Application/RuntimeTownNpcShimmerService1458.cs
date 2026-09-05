using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>
/// Authoritative TerrariaServer 1.4.5.8 AI_007 shimmer lifecycle for persistent town residents. The service owns
/// the pre-transform 0.01 transparency accumulation, state-25 landing search/delay/rise, invulnerability and the
/// final townNpcVariationIndex toggle. Presentation-only dust/light/particle effects remain client concerns.
/// </summary>
internal sealed class RuntimeTownNpcShimmerService1458
{
    private const float EnterThreshold = 0.9f;
    private const float EnterTransparency = 0.89f;
    private const float AccumulatePerTick = 0.01f;
    private const float FadeOutsideShimmerPerTick = 0.001f;
    private const float JustHitFade = 0.1f;
    private const float ExitFadePerTick = 1f / 60f;
    private const float RiseSpeed = 4f;
    private const int DistantLandingPixels = 560;
    private const int DistantLandingDelayTicks = 30;
    private const int BeginRiseTick = 30;
    private const int EarliestExitTick = 75;

    private readonly RuntimeNpcStore npcs;
    private readonly RuntimeTownNpcStateStore townNpcs;
    private readonly WorldTileStore tiles;
    private readonly RuntimeNpcReplicationRegistry? replication;
    private readonly Dictionary<NpcHandle, float> shimmerTransparency = [];
    private readonly NpcSnapshot[] active = new NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];

    public RuntimeTownNpcShimmerService1458(
        RuntimeNpcStore npcs,
        RuntimeTownNpcStateStore townNpcs,
        WorldTileStore tiles,
        RuntimeNpcReplicationRegistry? replication = null)
    {
        this.npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        this.townNpcs = townNpcs ?? throw new ArgumentNullException(nameof(townNpcs));
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));
        this.replication = replication;
    }

    public int Tick()
    {
        int committed = 0;
        int count = npcs.CopyActive(active);
        var liveHandles = new HashSet<NpcHandle>();
        for (int index = 0; index < count; index++)
        {
            NpcSnapshot snapshot = active[index];
            if (!NpcTypeId.TryCreate(snapshot.Type, out NpcTypeId type) ||
                !VanillaTownNpcShimmerCatalog1458.CanTogglePersistentTownVariant(type) ||
                !townNpcs.TryGet(snapshot.Handle.Slot, out WorldTownNpc? town) ||
                town.NetId != type.Value ||
                !VanillaTownNpcFacts1458.TryGetDefinition(type, out VanillaNpcDefinition definition) ||
                !definition.TryResolveHitbox(snapshot.Simulation.Scale, out VanillaNpcHitboxSize hitbox))
            {
                continue;
            }

            liveHandles.Add(snapshot.Handle);
            if (snapshot.Ai.Ai0 == 25f)
            {
                if (TickTransformState(in snapshot, town, type, in hitbox))
                    committed++;
                continue;
            }

            float transparency = shimmerTransparency.GetValueOrDefault(snapshot.Handle);
            if (snapshot.Simulation.LiquidContact == NpcLiquidContactKind.Shimmer)
            {
                transparency = Math.Clamp(transparency + AccumulatePerTick, 0f, 1f);
                if (transparency > EnterThreshold)
                {
                    if (TryEnterTransform(in snapshot, transparency, out NpcSnapshot entered))
                    {
                        shimmerTransparency[entered.Handle] = EnterTransparency;
                        townNpcs.TryUpdatePosition(entered.Handle.Slot, in entered);
                        committed++;
                    }
                    continue;
                }
            }
            else if (transparency > 0f)
            {
                if (snapshot.Simulation.JustHit)
                    transparency -= JustHitFade;
                transparency = Math.Max(0f, transparency - FadeOutsideShimmerPerTick);
            }

            if (transparency > 0f)
                shimmerTransparency[snapshot.Handle] = transparency;
            else
                shimmerTransparency.Remove(snapshot.Handle);
        }

        if (shimmerTransparency.Count != 0)
        {
            foreach (NpcHandle handle in shimmerTransparency.Keys.ToArray())
            {
                if (!liveHandles.Contains(handle))
                    shimmerTransparency.Remove(handle);
            }
        }

        return committed;
    }

    internal float GetTransparencyForTesting(NpcHandle handle) => shimmerTransparency.GetValueOrDefault(handle);

    private bool TryEnterTransform(
        in NpcSnapshot snapshot,
        float transparency,
        out NpcSnapshot entered)
    {
        _ = transparency;
        var ai = new NpcAiState(25f, 0f, 0f, 0f);
        NpcSimulationState simulation = snapshot.Simulation with
        {
            DontTakeDamage = true,
            NoGravity = true
        };
        var update = new NpcStateUpdate(
            snapshot.Type,
            snapshot.NetId,
            snapshot.PositionX,
            snapshot.PositionY,
            snapshot.VelocityX,
            snapshot.VelocityY,
            snapshot.Target,
            ai,
            simulation);
        return npcs.TryUpdate(snapshot.Handle, in update, out entered);
    }

    private bool TickTransformState(
        in NpcSnapshot snapshot,
        WorldTownNpc town,
        NpcTypeId type,
        in VanillaNpcHitboxSize hitbox)
    {
        float transparency = shimmerTransparency.TryGetValue(snapshot.Handle, out float tracked)
            ? tracked
            : EnterTransparency;
        float positionX = snapshot.PositionX;
        float positionY = snapshot.PositionY;
        float velocityX = snapshot.VelocityX;
        float velocityY = snapshot.VelocityY;
        NpcAiState ai = snapshot.Ai;

        if (ai.Ai1 == 0f)
        {
            velocityX = 0f;
            if (ai.Ai2 < 1f && VanillaWorldShimmerLanding1458.TryFind(
                    tiles,
                    positionX,
                    positionY,
                    hitbox.Width,
                    hitbox.Height,
                    town.Homeless,
                    town.HomeTileX,
                    town.HomeTileY,
                    out float landingX,
                    out float landingY))
            {
                float dx = landingX - positionX;
                float dy = landingY - positionY;
                positionX = landingX;
                positionY = landingY;
                if (MathF.Sqrt(dx * dx + dy * dy) >= DistantLandingPixels)
                    ai = ai with { Ai2 = DistantLandingDelayTicks };
            }
        }

        if (ai.Ai2 > 0f)
        {
            float nextDelay = ai.Ai2 - 1f;
            ai = ai with
            {
                Ai1 = nextDelay <= 0f ? 1f : ai.Ai1,
                Ai2 = nextDelay
            };
            return CommitTransformState(
                in snapshot, positionX, positionY, velocityX, velocityY, ai, transparency, finishing: false, type);
        }

        ai = ai with { Ai1 = ai.Ai1 + 1f };
        if (ai.Ai1 >= BeginRiseTick)
        {
            VanillaLiquidContactState contact = VanillaWorldCollision.GetLiquidContacts(
                tiles,
                positionX,
                positionY,
                hitbox.Width,
                hitbox.Height);
            if (!contact.Wet)
                transparency = Math.Clamp(transparency - ExitFadePerTick, 0f, 1f);
            else
                ai = ai with { Ai1 = BeginRiseTick };
            velocityX = 0f;
            velocityY = -RiseSpeed * transparency;
        }

        bool finishing = ai.Ai1 >= EarliestExitTick && transparency <= 0f;
        return CommitTransformState(
            in snapshot, positionX, positionY, velocityX, velocityY, ai, transparency, finishing, type);
    }

    private bool CommitTransformState(
        in NpcSnapshot snapshot,
        float positionX,
        float positionY,
        float velocityX,
        float velocityY,
        NpcAiState ai,
        float transparency,
        bool finishing,
        NpcTypeId type)
    {
        NpcSimulationState simulation;
        if (finishing)
        {
            ai = default;
            velocityX = 0f;
            velocityY = -RiseSpeed;
            simulation = snapshot.Simulation with
            {
                Wet = false,
                LiquidContact = NpcLiquidContactKind.None,
                DontTakeDamage = false,
                NoGravity = false,
                LocalAi = default,
                OldPositionX = snapshot.PositionX,
                OldPositionY = snapshot.PositionY,
                OldVelocityX = snapshot.VelocityX,
                OldVelocityY = snapshot.VelocityY
            };
        }
        else
        {
            simulation = snapshot.Simulation with
            {
                Wet = false,
                LiquidContact = NpcLiquidContactKind.None,
                DontTakeDamage = true,
                NoGravity = true,
                OldPositionX = snapshot.PositionX,
                OldPositionY = snapshot.PositionY,
                OldVelocityX = snapshot.VelocityX,
                OldVelocityY = snapshot.VelocityY
            };
        }

        var update = new NpcStateUpdate(
            snapshot.Type,
            snapshot.NetId,
            positionX,
            positionY,
            velocityX,
            velocityY,
            snapshot.Target,
            ai,
            simulation);
        if (!npcs.TryUpdate(snapshot.Handle, in update, out NpcSnapshot committed))
            return false;

        townNpcs.TryUpdatePosition(committed.Handle.Slot, in committed);
        if (!finishing)
        {
            shimmerTransparency[committed.Handle] = transparency;
            return true;
        }

        shimmerTransparency.Remove(committed.Handle);
        if (!townNpcs.TryToggleShimmerVariation(committed.Handle.Slot, type, in committed, out RuntimeTownNpcIdentityCommit identity))
            throw new InvalidOperationException("Committed town shimmer transition could not update the persistent variation state.");
        replication?.TryPublishTownIdentity(in identity);
        return true;
    }
}
