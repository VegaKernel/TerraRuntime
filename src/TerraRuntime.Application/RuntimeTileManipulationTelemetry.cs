using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

/// <summary>
/// Fixed process-lifetime observations for tracing packet-17 proposals through the authenticated network boundary,
/// bounded command ingress and authoritative tile owner. These counters are diagnostic only; they never decide whether
/// a command is accepted.
/// </summary>
internal enum ToolCommitFailureKind : byte
{
    NotCurrent,
    NoSourceStage,
    NoCandidate,
    Incomplete,
    Ambiguous,
    SealRejected,
    AdoptRejected
}

internal enum RemotePhaseFailureKind : byte
{
    InitialState,
    Census,
    HealthContext,
    ItemState,
    OutsideEffects,
    HeatOrCooldown,
    Inventory,
    Placement,
    Selection,
    Combat,
    BuildingContext,
    Health,
    Mana,
    TilePrefix,
    Physics,
    ToolReach,
    ToolStage,
    ToolUseState,
    ToolTimeState,
    ToolResolve,
    Unknown
}

internal enum ToolCaptureFailureKind : byte
{
    World,
    Random,
    Projectile,
    Profile,
    Buffs,
    Dead,
    Phase,
    Combat,
    Inventory,
    BuffState,
    Selected
}

internal sealed class RuntimeTileManipulationTelemetry
{
    public static RuntimeTileManipulationTelemetry Shared { get; } = new();

    private long packet17Received;
    private long packet17DecodeRejected;
    private long commandPosted;
    private long commandPostRejected;
    private long authorityEntered;
    private long toolObserved;
    private long toolRetained;
    private long toolRejected;
    private long captureWorld;
    private long captureRandom;
    private long captureProjectile;
    private long captureProfile;
    private long captureBuffs;
    private long captureDead;
    private long capturePhase;
    private long captureCombat;
    private long captureInventory;
    private long captureBuffState;
    private long captureSelected;
    private long remotePhaseFailures;
    private int remotePhaseLastFailure = -1;
    private int remotePhaseLastLine;
    private int remoteEquipmentLastFailure = -1;
    private int remoteEquipmentLastSlot = -1;
    private int remoteEquipmentLastItem = -1;
    private int remoteEquipmentLastPrefix = -1;
    private int toolPreparationLastLine;
    private int toolMatchExpectedCount;
    private int toolMatchObservedCount;
    private int toolMatchExpectedAction = -1;
    private int toolMatchObservedAction = -1;
    private int toolMatchExpectedData;
    private int toolMatchObservedData;
    private int toolPreparationTileActive;
    private int toolPreparationTileType;
    private int toolPreparationPick;
    private int toolPreparationHammer;
    private int toolPreparationAxe;
    private long remotePhaseInitialState;
    private long remotePhaseInventory;
    private long remotePhasePlacement;
    private long remotePhaseTilePrefix;
    private long remotePhasePhysics;
    private long remotePhaseToolReach;
    private long remotePhaseToolStage;
    private long remotePhaseToolUseState;
    private long remotePhaseToolTimeState;
    private long remotePhaseToolResolve;
    private long toolCommitted;
    private long commitAttempts;
    private long commitNotCurrent;
    private long commitNoSourceStage;
    private long commitNoCandidate;
    private long commitIncomplete;
    private long commitAmbiguous;
    private long commitSealRejected;
    private long commitAdoptRejected;
    private int lastCommitInputCount;
    private int lastCommitInputAction = -1;
    private int lastCommitInputData;
    private long pickingReceived;
    private long pickingRetained;
    private long pickingRejected;
    private long corrections;
    private long relayedFrames;
    private long relayRejected;
    private long lastConnection;
    private long lastAction;
    private long lastTileX;
    private long lastTileY;
    private long lastData;
    private long lastStyle;

    public void RecordPacket17(long connectionId, in TerrariaTileManipulationState state)
    {
        Interlocked.Increment(ref packet17Received);
        RecordLast(connectionId, in state);
    }

    public void RecordDecodeRejected(long connectionId)
    {
        Interlocked.Increment(ref packet17DecodeRejected);
        Volatile.Write(ref lastConnection, connectionId);
    }

    public void RecordCommandPost(long connectionId, in TerrariaTileManipulationState state, bool accepted)
    {
        if (accepted)
            Interlocked.Increment(ref commandPosted);
        else
            Interlocked.Increment(ref commandPostRejected);
        RecordLast(connectionId, in state);
    }

    public void RecordAuthorityEntry(long connectionId, in TerrariaTileManipulationState state)
    {
        Interlocked.Increment(ref authorityEntered);
        RecordLast(connectionId, in state);
    }

    public void RecordToolObservation(bool retained)
    {
        Interlocked.Increment(ref toolObserved);
        if (retained)
            Interlocked.Increment(ref toolRetained);
        else
            Interlocked.Increment(ref toolRejected);
    }

    public void RecordToolCommit() => Interlocked.Increment(ref toolCommitted);

    public void RecordRemoteEquipmentFailure(int reason, in PlayerEquipmentCommitRequest item)
    {
        Volatile.Write(ref remoteEquipmentLastFailure, reason);
        Volatile.Write(ref remoteEquipmentLastSlot, item.SlotId);
        Volatile.Write(ref remoteEquipmentLastItem, item.ItemNetId);
        Volatile.Write(ref remoteEquipmentLastPrefix, item.PrefixId.Value);
    }

    public void RecordRemoteEquipmentFailure(int reason)
    {
        Volatile.Write(ref remoteEquipmentLastFailure, reason);
        Volatile.Write(ref remoteEquipmentLastSlot, -1);
        Volatile.Write(ref remoteEquipmentLastItem, -1);
        Volatile.Write(ref remoteEquipmentLastPrefix, -1);
    }

    public void RecordRemotePhaseFailure(RemotePhaseFailureKind kind, int line = 0)
    {
        Volatile.Write(ref remotePhaseLastFailure, (int)kind);
        Volatile.Write(ref remotePhaseLastLine, line);
        Interlocked.Increment(ref remotePhaseFailures);
        switch (kind)
        {
            case RemotePhaseFailureKind.InitialState: Interlocked.Increment(ref remotePhaseInitialState); break;
            case RemotePhaseFailureKind.Inventory: Interlocked.Increment(ref remotePhaseInventory); break;
            case RemotePhaseFailureKind.Placement: Interlocked.Increment(ref remotePhasePlacement); break;
            case RemotePhaseFailureKind.TilePrefix: Interlocked.Increment(ref remotePhaseTilePrefix); break;
            case RemotePhaseFailureKind.Physics: Interlocked.Increment(ref remotePhasePhysics); break;
            case RemotePhaseFailureKind.ToolReach: Interlocked.Increment(ref remotePhaseToolReach); break;
            case RemotePhaseFailureKind.ToolStage: Interlocked.Increment(ref remotePhaseToolStage); break;
            case RemotePhaseFailureKind.ToolUseState: Interlocked.Increment(ref remotePhaseToolUseState); break;
            case RemotePhaseFailureKind.ToolTimeState: Interlocked.Increment(ref remotePhaseToolTimeState); break;
            case RemotePhaseFailureKind.ToolResolve: Interlocked.Increment(ref remotePhaseToolResolve); break;
        }
    }

    public void RecordCaptureFailure(ToolCaptureFailureKind kind)
    {
        switch (kind)
        {
            case ToolCaptureFailureKind.World: Interlocked.Increment(ref captureWorld); break;
            case ToolCaptureFailureKind.Random: Interlocked.Increment(ref captureRandom); break;
            case ToolCaptureFailureKind.Projectile: Interlocked.Increment(ref captureProjectile); break;
            case ToolCaptureFailureKind.Profile: Interlocked.Increment(ref captureProfile); break;
            case ToolCaptureFailureKind.Buffs: Interlocked.Increment(ref captureBuffs); break;
            case ToolCaptureFailureKind.Dead: Interlocked.Increment(ref captureDead); break;
            case ToolCaptureFailureKind.Phase: Interlocked.Increment(ref capturePhase); break;
            case ToolCaptureFailureKind.Combat: Interlocked.Increment(ref captureCombat); break;
            case ToolCaptureFailureKind.Inventory: Interlocked.Increment(ref captureInventory); break;
            case ToolCaptureFailureKind.BuffState: Interlocked.Increment(ref captureBuffState); break;
            case ToolCaptureFailureKind.Selected: Interlocked.Increment(ref captureSelected); break;
        }
    }

    public void RecordCommitAttempt() => Interlocked.Increment(ref commitAttempts);

    public void RecordToolPreparationFailure(int line)
    {
        Volatile.Write(ref toolPreparationLastLine, line);
    }

    public void RecordToolPreparationTile(bool active, int tileType, int pick, int hammer, int axe)
    {
        Volatile.Write(ref toolPreparationTileActive, active ? 1 : 0);
        Volatile.Write(ref toolPreparationTileType, tileType);
        Volatile.Write(ref toolPreparationPick, pick);
        Volatile.Write(ref toolPreparationHammer, hammer);
        Volatile.Write(ref toolPreparationAxe, axe);
    }

    public void RecordToolMatch(int expectedCount, int observedCount, int expectedAction, int observedAction,
        int expectedData, int observedData)
    {
        Volatile.Write(ref toolMatchExpectedCount, expectedCount);
        Volatile.Write(ref toolMatchObservedCount, observedCount);
        Volatile.Write(ref toolMatchExpectedAction, expectedAction);
        Volatile.Write(ref toolMatchObservedAction, observedAction);
        Volatile.Write(ref toolMatchExpectedData, expectedData);
        Volatile.Write(ref toolMatchObservedData, observedData);
    }

    public void RecordCommitInput(int count, int action, int data)
    {
        Volatile.Write(ref lastCommitInputCount, count);
        Volatile.Write(ref lastCommitInputAction, action);
        Volatile.Write(ref lastCommitInputData, data);
    }

    public void RecordCommitFailure(ToolCommitFailureKind kind)
    {
        switch (kind)
        {
            case ToolCommitFailureKind.NotCurrent: Interlocked.Increment(ref commitNotCurrent); break;
            case ToolCommitFailureKind.NoSourceStage: Interlocked.Increment(ref commitNoSourceStage); break;
            case ToolCommitFailureKind.NoCandidate: Interlocked.Increment(ref commitNoCandidate); break;
            case ToolCommitFailureKind.Incomplete: Interlocked.Increment(ref commitIncomplete); break;
            case ToolCommitFailureKind.Ambiguous: Interlocked.Increment(ref commitAmbiguous); break;
            case ToolCommitFailureKind.SealRejected: Interlocked.Increment(ref commitSealRejected); break;
            case ToolCommitFailureKind.AdoptRejected: Interlocked.Increment(ref commitAdoptRejected); break;
        }
    }

    public void RecordPicking(bool retained)
    {
        Interlocked.Increment(ref pickingReceived);
        if (retained)
            Interlocked.Increment(ref pickingRetained);
        else
            Interlocked.Increment(ref pickingRejected);
    }

    public void RecordCorrection() => Interlocked.Increment(ref corrections);

    public void RecordRelay(bool accepted)
    {
        if (accepted)
            Interlocked.Increment(ref relayedFrames);
        else
            Interlocked.Increment(ref relayRejected);
    }

    public RuntimeTileManipulationTelemetrySnapshot Capture() => new(
        Packet17Received: Interlocked.Read(ref packet17Received),
        Packet17DecodeRejected: Interlocked.Read(ref packet17DecodeRejected),
        CommandPosted: Interlocked.Read(ref commandPosted),
        CommandPostRejected: Interlocked.Read(ref commandPostRejected),
        AuthorityEntered: Interlocked.Read(ref authorityEntered),
        ToolObserved: Interlocked.Read(ref toolObserved),
        ToolRetained: Interlocked.Read(ref toolRetained),
        ToolRejected: Interlocked.Read(ref toolRejected),
        CaptureWorld: Interlocked.Read(ref captureWorld),
        CaptureRandom: Interlocked.Read(ref captureRandom),
        CaptureProjectile: Interlocked.Read(ref captureProjectile),
        CaptureProfile: Interlocked.Read(ref captureProfile),
        CaptureBuffs: Interlocked.Read(ref captureBuffs),
        CaptureDead: Interlocked.Read(ref captureDead),
        CapturePhase: Interlocked.Read(ref capturePhase),
        CaptureCombat: Interlocked.Read(ref captureCombat),
        CaptureInventory: Interlocked.Read(ref captureInventory),
        CaptureBuffState: Interlocked.Read(ref captureBuffState),
        CaptureSelected: Interlocked.Read(ref captureSelected),
        RemotePhaseFailures: Interlocked.Read(ref remotePhaseFailures),
        RemotePhaseLastFailure: Volatile.Read(ref remotePhaseLastFailure),
        RemotePhaseLastLine: Volatile.Read(ref remotePhaseLastLine),
         RemoteEquipmentLastFailure: Volatile.Read(ref remoteEquipmentLastFailure),
         RemoteEquipmentLastSlot: Volatile.Read(ref remoteEquipmentLastSlot),
         RemoteEquipmentLastItem: Volatile.Read(ref remoteEquipmentLastItem),
         RemoteEquipmentLastPrefix: Volatile.Read(ref remoteEquipmentLastPrefix),
        ToolPreparationLastLine: Volatile.Read(ref toolPreparationLastLine),
        LastCommitInputCount: Volatile.Read(ref lastCommitInputCount),
        LastCommitInputAction: Volatile.Read(ref lastCommitInputAction),
        LastCommitInputData: Volatile.Read(ref lastCommitInputData),
        ToolMatchExpectedCount: Volatile.Read(ref toolMatchExpectedCount),
        ToolMatchObservedCount: Volatile.Read(ref toolMatchObservedCount),
        ToolMatchExpectedAction: Volatile.Read(ref toolMatchExpectedAction),
        ToolMatchObservedAction: Volatile.Read(ref toolMatchObservedAction),
        ToolMatchExpectedData: Volatile.Read(ref toolMatchExpectedData),
        ToolMatchObservedData: Volatile.Read(ref toolMatchObservedData),
        ToolPreparationTileActive: Volatile.Read(ref toolPreparationTileActive),
        ToolPreparationTileType: Volatile.Read(ref toolPreparationTileType),
        ToolPreparationPick: Volatile.Read(ref toolPreparationPick),
        ToolPreparationHammer: Volatile.Read(ref toolPreparationHammer),
        ToolPreparationAxe: Volatile.Read(ref toolPreparationAxe),
        RemotePhaseInitialState: Interlocked.Read(ref remotePhaseInitialState),
        RemotePhaseInventory: Interlocked.Read(ref remotePhaseInventory),
        RemotePhasePlacement: Interlocked.Read(ref remotePhasePlacement),
        RemotePhaseTilePrefix: Interlocked.Read(ref remotePhaseTilePrefix),
        RemotePhasePhysics: Interlocked.Read(ref remotePhasePhysics),
        RemotePhaseToolReach: Interlocked.Read(ref remotePhaseToolReach),
        RemotePhaseToolStage: Interlocked.Read(ref remotePhaseToolStage),
        RemotePhaseToolUseState: Interlocked.Read(ref remotePhaseToolUseState),
        RemotePhaseToolTimeState: Interlocked.Read(ref remotePhaseToolTimeState),
        RemotePhaseToolResolve: Interlocked.Read(ref remotePhaseToolResolve),
        ToolCommitted: Interlocked.Read(ref toolCommitted),
        CommitAttempts: Interlocked.Read(ref commitAttempts),
        CommitNotCurrent: Interlocked.Read(ref commitNotCurrent),
        CommitNoSourceStage: Interlocked.Read(ref commitNoSourceStage),
        CommitNoCandidate: Interlocked.Read(ref commitNoCandidate),
        CommitIncomplete: Interlocked.Read(ref commitIncomplete),
        CommitAmbiguous: Interlocked.Read(ref commitAmbiguous),
        CommitSealRejected: Interlocked.Read(ref commitSealRejected),
        CommitAdoptRejected: Interlocked.Read(ref commitAdoptRejected),
        PickingReceived: Interlocked.Read(ref pickingReceived),
        PickingRetained: Interlocked.Read(ref pickingRetained),
        PickingRejected: Interlocked.Read(ref pickingRejected),
        Corrections: Interlocked.Read(ref corrections),
        RelayedFrames: Interlocked.Read(ref relayedFrames),
        RelayRejected: Interlocked.Read(ref relayRejected),
        LastConnection: Volatile.Read(ref lastConnection),
        LastAction: Volatile.Read(ref lastAction),
        LastTileX: Volatile.Read(ref lastTileX),
        LastTileY: Volatile.Read(ref lastTileY),
        LastData: Volatile.Read(ref lastData),
        LastStyle: Volatile.Read(ref lastStyle));

    private void RecordLast(long connectionId, in TerrariaTileManipulationState state)
    {
        Volatile.Write(ref lastConnection, connectionId);
        Volatile.Write(ref lastAction, state.Action);
        Volatile.Write(ref lastTileX, state.TileX);
        Volatile.Write(ref lastTileY, state.TileY);
        Volatile.Write(ref lastData, state.Data);
        Volatile.Write(ref lastStyle, state.Style);
    }
}

internal readonly record struct RuntimeTileManipulationTelemetrySnapshot(
    long Packet17Received,
    long Packet17DecodeRejected,
    long CommandPosted,
    long CommandPostRejected,
    long AuthorityEntered,
    long ToolObserved,
    long ToolRetained,
    long ToolRejected,
    long CaptureWorld,
    long CaptureRandom,
    long CaptureProjectile,
    long CaptureProfile,
    long CaptureBuffs,
    long CaptureDead,
    long CapturePhase,
    long CaptureCombat,
    long CaptureInventory,
    long CaptureBuffState,
    long CaptureSelected,
    long RemotePhaseFailures,
    int RemotePhaseLastFailure,
    int RemotePhaseLastLine,
    int RemoteEquipmentLastFailure,
    int RemoteEquipmentLastSlot,
    int RemoteEquipmentLastItem,
    int RemoteEquipmentLastPrefix,
    int ToolPreparationLastLine,
    int LastCommitInputCount,
    int LastCommitInputAction,
    int LastCommitInputData,
    int ToolMatchExpectedCount,
    int ToolMatchObservedCount,
    int ToolMatchExpectedAction,
    int ToolMatchObservedAction,
    int ToolMatchExpectedData,
    int ToolMatchObservedData,
    int ToolPreparationTileActive,
    int ToolPreparationTileType,
    int ToolPreparationPick,
    int ToolPreparationHammer,
    int ToolPreparationAxe,
    long RemotePhaseInitialState,
    long RemotePhaseInventory,
    long RemotePhasePlacement,
    long RemotePhaseTilePrefix,
    long RemotePhasePhysics,
    long RemotePhaseToolReach,
    long RemotePhaseToolStage,
    long RemotePhaseToolUseState,
    long RemotePhaseToolTimeState,
    long RemotePhaseToolResolve,
    long ToolCommitted,
    long CommitAttempts,
    long CommitNotCurrent,
    long CommitNoSourceStage,
    long CommitNoCandidate,
    long CommitIncomplete,
    long CommitAmbiguous,
    long CommitSealRejected,
    long CommitAdoptRejected,
    long PickingReceived,
    long PickingRetained,
    long PickingRejected,
    long Corrections,
    long RelayedFrames,
    long RelayRejected,
    long LastConnection,
    long LastAction,
    long LastTileX,
    long LastTileY,
    long LastData,
    long LastStyle)
{
    public string FormatForLog(
        long authorityRequests,
        long validated,
        long applied,
        long rejected,
        long unsupported,
        long retainedReports,
        long rejectedReports,
        long committedActions) =>
        $"packet17_received={Packet17Received} packet17_decode_rejected={Packet17DecodeRejected} " +
        $"command_posted={CommandPosted} command_post_rejected={CommandPostRejected} " +
        $"authority_entered={AuthorityEntered} authority_requests={authorityRequests} validated={validated} " +
        $"applied={applied} rejected={rejected} unsupported={unsupported} " +
        $"tool_observed={ToolObserved} tool_retained={ToolRetained} tool_rejected={ToolRejected} " +
        $"capture_world={CaptureWorld} capture_random={CaptureRandom} capture_projectile={CaptureProjectile} " +
        $"capture_profile={CaptureProfile} capture_buffs={CaptureBuffs} capture_dead={CaptureDead} " +
        $"capture_phase={CapturePhase} capture_combat={CaptureCombat} capture_inventory={CaptureInventory} " +
        $"capture_buff_state={CaptureBuffState} capture_selected={CaptureSelected} " +
        $"phase_failures={RemotePhaseFailures} phase_last={RemotePhaseLastFailure} phase_line={RemotePhaseLastLine} equipment_failure={RemoteEquipmentLastFailure}/{RemoteEquipmentLastSlot}/{RemoteEquipmentLastItem}/{RemoteEquipmentLastPrefix} prep_line={ToolPreparationLastLine} commit_input={LastCommitInputCount}/{LastCommitInputAction}/{LastCommitInputData} match={ToolMatchExpectedCount}/{ToolMatchObservedCount}/{ToolMatchExpectedAction}/{ToolMatchObservedAction}/{ToolMatchExpectedData}/{ToolMatchObservedData} prep_tile={ToolPreparationTileActive}/{ToolPreparationTileType}/{ToolPreparationPick}/{ToolPreparationHammer}/{ToolPreparationAxe} phase_initial={RemotePhaseInitialState} " +
         $"phase_inventory={RemotePhaseInventory} phase_placement={RemotePhasePlacement} " +
         $"phase_prefix={RemotePhaseTilePrefix} phase_physics={RemotePhasePhysics} " +
         $"phase_reach={RemotePhaseToolReach} phase_tool_stage={RemotePhaseToolStage} phase_tool_use={RemotePhaseToolUseState} phase_tool_time={RemotePhaseToolTimeState} phase_tool_resolve={RemotePhaseToolResolve} " +
         $"retained_reports={retainedReports} rejected_reports={rejectedReports} " +
        $"tool_committed={ToolCommitted} committed_actions={committedActions} " +
        $"commit_attempts={CommitAttempts} commit_not_current={CommitNotCurrent} " +
        $"commit_no_source_stage={CommitNoSourceStage} commit_no_candidate={CommitNoCandidate} " +
        $"commit_incomplete={CommitIncomplete} commit_ambiguous={CommitAmbiguous} " +
        $"commit_seal_rejected={CommitSealRejected} commit_adopt_rejected={CommitAdoptRejected} " +
        $"picking_received={PickingReceived} picking_retained={PickingRetained} picking_rejected={PickingRejected} " +
        $"corrections={Corrections} relay={RelayedFrames} relay_rejected={RelayRejected} " +
        $"last_connection={LastConnection} last_action={LastAction} last_tile=({LastTileX},{LastTileY}) " +
        $"last_data={LastData} last_style={LastStyle}";
}
