namespace TerraRuntime.Gameplay.Worlds;

public readonly record struct InvasionState1458(
    int Type, int Size, int SizeStart, int Delay, double X, int Warning,
    int Progress, int ProgressMax, int ProgressIcon, int ProgressWave,
    bool DownedGoblin, bool DownedPirate, bool DownedMartian, bool LanternNextNight);

public enum InvasionWarningPhase1458 : byte { None, Ended, West, East, Arrived }

public readonly record struct InvasionWarning1458(int Type, InvasionWarningPhase1458 Phase);

public readonly record struct InvasionEffects1458(
    int Warnings = 0, int ProgressionEvent = 0, bool WorldInfo = false, bool Progress = false,
    InvasionWarning1458 FirstWarning = default, InvasionWarning1458 SecondWarning = default,
    InvasionWarning1458 ThirdWarning = default);

public readonly record struct InvasionTransition1458(InvasionState1458 State, InvasionEffects1458 Effects);

/// <summary>Source-pinned ordinary invasion transitions; scheduling, publication and actors remain runtime-owned.</summary>
public static class VanillaInvasionLifecycle1458
{
    public static bool CanOwnLive(in InvasionState1458 state) =>
        state.Type is 0 or 1 or 3 or 4 && double.IsFinite(state.X) && state.SizeStart >= 0;

    public static bool CanStart(in InvasionState1458 state, int eligiblePlayers, bool ignoreDelay = false) =>
        CanOwnLive(in state) && state.Type == 0 && (ignoreDelay || state.Delay == 0) &&
        eligiblePlayers is > 0 and <= 255;

    public static bool TryStart(in InvasionState1458 state, int type, int eligiblePlayers,
        int maximumTilesX, int spawnTileX, Func<int, int, int> next, out InvasionTransition1458 transition)
    {
        transition = default;
        ArgumentNullException.ThrowIfNull(next);
        if (!CanOwnLive(in state) || type is not (1 or 3 or 4) || eligiblePlayers is < 0 or > 255 ||
            maximumTilesX <= 0 || spawnTileX < 0 || spawnTileX >= maximumTilesX) return false;
        var current = state.Type != 0 && state.Size == 0 ? state with { Type = 0 } : state;
        if (current.Type != 0 || eligiblePlayers == 0)
        {
            transition = new(current, default);
            return true;
        }
        int size = type switch { 3 => 120 + 60 * eligiblePlayers, 4 => 160 + 40 * eligiblePlayers,
            _ => 80 + 40 * eligiblePlayers };
        double x = type == 4 ? spawnTileX - 1 : next(0, 2) == 0 ? 0 : maximumTilesX;
        transition = new(current with { Type = type, Size = size, SizeStart = size,
            Progress = 0, ProgressMax = size, ProgressIcon = type + 3, ProgressWave = 0,
            X = x, Warning = type == 4 ? 2 : 0 }, default);
        return true;
    }

    public static bool TryAdvance(in InvasionState1458 state, int spawnTileX, int dayRate,
        out InvasionTransition1458 transition)
    {
        transition = default;
        if (!CanOwnLive(in state) || spawnTileX < 0) return false;
        if (state.Type <= 0)
        {
            transition = new(state, default);
            return true;
        }
        var next = state;
        int warnings = 0, achievement = 0;
        InvasionWarning1458 firstWarning = default, secondWarning = default, thirdWarning = default;
        void Warn(in InvasionState1458 warningState)
        {
            var phase = warningState.Size <= 0 ? InvasionWarningPhase1458.Ended :
                warningState.X < spawnTileX ? InvasionWarningPhase1458.West :
                warningState.X > spawnTileX ? InvasionWarningPhase1458.East : InvasionWarningPhase1458.Arrived;
            var warning = new InvasionWarning1458(warningState.Type == 0 ? 1 : warningState.Type, phase);
            if (warnings == 0) firstWarning = warning;
            else if (warnings == 1) secondWarning = warning;
            else thirdWarning = warning;
            warnings++;
        }
        bool completed = state.Size <= 0;
        if (completed)
        {
            bool first = state.Type switch { 1 => !state.DownedGoblin, 3 => !state.DownedPirate,
                _ => !state.DownedMartian };
            next = next with { Type = 0, Delay = 0,
                DownedGoblin = state.DownedGoblin || state.Type == 1,
                DownedPirate = state.DownedPirate || state.Type == 3,
                DownedMartian = state.DownedMartian || state.Type == 4,
                LanternNextNight = state.LanternNextNight || first };
            achievement = state.Type switch { 1 => 10, 3 => 11, _ => 13 };
            Warn(in state);
        }
        // Source does not return after clearing the invasion. Movement and warning cadence still run.
        if (next.X != spawnTileX)
        {
            float amount = Math.Max((float)dayRate, 1f);
            bool arrives = next.X > spawnTileX ? next.X - amount <= spawnTileX : next.X + amount >= spawnTileX;
            if (arrives)
            {
                next = next with { X = spawnTileX };
                Warn(in next);
            }
            else
            {
                if (next.Warning == int.MinValue) return false;
                next = next with { X = next.X > spawnTileX ? next.X - amount : next.X + amount,
                    Warning = next.Warning - 1 };
            }
            if (next.Warning <= 0)
            {
                next = next with { Warning = 3600 };
                Warn(in next);
            }
        }
        transition = new(next, new(warnings, achievement, completed, false,
            firstWarning, secondWarning, thirdWarning));
        return true;
    }

    public static bool TryCreditDeath(in InvasionState1458 state, int npcType,
        out InvasionTransition1458 transition)
    {
        transition = default;
        if (!CanOwnLive(in state)) return false;
        int group = GetGroup(npcType);
        int points = npcType switch { 216 => 5, 395 or 491 or 471 => 10, 472 or 387 => 0, _ => 1 };
        if (group <= 0 || group != state.Type || points == 0)
        {
            transition = new(state, default);
            return true;
        }
        long remaining = (long)state.Size - points;
        if (remaining < int.MinValue) return false;
        int size = Math.Max(0, (int)remaining);
        long progress = (long)state.SizeStart - size;
        if (progress is < int.MinValue or > int.MaxValue) return false;
        transition = new(state with { Size = size, Progress = (int)progress,
            ProgressMax = state.SizeStart, ProgressIcon = group + 3, ProgressWave = 0 }, new(Progress: true));
        return true;
    }

    public static int GetGroup(int npcType) => npcType switch
    {
        26 or 27 or 28 or 29 or 111 or 471 or 472 => 1,
        143 or 144 or 145 => 2,
        212 or 213 or 214 or 215 or 216 or 252 or 491 or 492 or 662 => 3,
        381 or 382 or 383 or 385 or 386 or 387 or 388 or 389 or 390 or 391 or 394 or 395 or 520 => 4,
        _ => 0
    };

    // Legacy world-load recovery remains queryable, including Frost2; current326 loads its stored SizeStart.
    public static bool TryRecoverLegacySizeStart(int type, int size, out int sizeStart)
    {
        sizeStart = 0;
        int baseline = type switch { 1 or 2 => 80, 3 => 120, 4 => 160, _ => 0 };
        int unit = type == 3 ? 60 : 40;
        if (baseline == 0) return false;
        long delta = (long)size - baseline;
        if (delta is < int.MinValue or > int.MaxValue) return false;
        float quotient = (float)(int)delta / unit;
        double bands = Math.Ceiling(quotient);
        long recovered = baseline + (bands > 0 ? (long)bands * unit : 0);
        if (recovered > int.MaxValue) return false;
        sizeStart = (int)recovered;
        return true;
    }
}
