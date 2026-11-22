namespace TerraRuntime.World;

public sealed partial class WorldWiringCircuit1458
{
    private const ushort LogicLamp = 419, LogicGate = 420;
    // A hostile trigger cannot retain an unbounded nested cascade. This is a runtime
    // work ceiling, not a vanilla wire-length rule; exhausted pulses retain committed cells.
    private const int LogicEventLimit = 65536;
    private readonly Func<int, int>? _nextLogicSample;
    private readonly Action<int, int>? _logicSmoke;
    private readonly Queue<int> _logicLamps = new();
    private Queue<int> _gatesCurrent = new(), _gatesNext = new();
    private readonly HashSet<int> _gatesDone = new();
    private int _logicEvents;
    private bool drainingLogic;

    public bool LastLogicBudgetExceeded { get; private set; }

    /// <summary>Evaluates an existing lamp column without toggling a lamp; never reenters a live pulse.</summary>
    public bool PokeLogicGate(int x, int y)
    {
        if (_running || !ContainsMechanism(x, y) || _nextLogicSample is null || _logicSmoke is null) return false;
        _running = true;
        BeginLogicPulse();
        try { QueueLogicLamp(x, y); DrainLogicGates(); return true; }
        finally { ClearPulseState(); }
    }

    private void BeginLogicPulse()
    {
        _logicEvents = 0;
        LastLogicBudgetExceeded = false;
    }

    private bool SpendLogicEvent()
    {
        if (_logicEvents < LogicEventLimit) { _logicEvents++; return true; }
        LastLogicBudgetExceeded = true;
        return false;
    }

    private void QueueLogicLamp(int x, int y)
    {
        if (SpendLogicEvent()) _logicLamps.Enqueue(Index(x, y));
    }

    private bool HitLogicLamp(int x, int y)
    {
        if (_nextLogicSample is null || _logicSmoke is null || !CanFrameLogic(x, y)) return false;
        var tile = _tiles.Get(x, y);
        if (!tile.IsActive || tile.Type != LogicLamp) return false;
        if (tile.FrameX != SwitchFrameWidth)
            tile.FrameX = tile.FrameX >= SourceFramePitch ? (short)(tile.FrameX - SourceFramePitch) : (short)(tile.FrameX + SourceFramePitch);
        SkipWire(x, y);
        _tiles.Set(x, y, in tile);
        _publish?.Invoke(x, y, 1, 1);
        QueueLogicLamp(x, y);
        return true;
    }

    private void DrainLogicGates()
    {
        // Source leaves its active gate at the queue head until its pulse returns.
        // Retain that recursion guard even though this runtime dequeues before dispatch.
        if (drainingLogic) return;
        drainingLogic = true;
        try
        {
            // Wiring.LogicGatePass 1.4.5.8 drains lamps before a gate wave. A gate's
            // nested four-color pulse only queues lamps; the next wave sees their final state.
            while (!LastLogicBudgetExceeded && _logicLamps.Count != 0)
            {
                while (!LastLogicBudgetExceeded && _logicLamps.TryDequeue(out int lamp)) CheckLogicGate(lamp);
                while (!LastLogicBudgetExceeded && _gatesNext.Count != 0)
                {
                    (_gatesCurrent, _gatesNext) = (_gatesNext, _gatesCurrent);
                    while (!LastLogicBudgetExceeded && _gatesCurrent.TryDequeue(out int gate))
                    {
                        if (!_gatesDone.Add(gate)) continue;
                        int x = gate / _tiles.Dimensions.HeightTiles, y = gate % _tiles.Dimensions.HeightTiles;
                        RunWirePulse(x, y, 1, 1);
                    }
                }
            }
        }
        finally { drainingLogic = false; }
    }

    private void CheckLogicGate(int lamp)
    {
        int x = lamp / _tiles.Dimensions.HeightTiles, originY = lamp % _tiles.Dimensions.HeightTiles;
        if (!ContainsMechanism(x, originY)) return;
        for (int y = originY; y < _tiles.Dimensions.HeightTiles; y++)
        {
            var gate = _tiles.Get(x, y);
            if (!gate.IsActive) return;
            if (gate.Type != LogicGate) { if (gate.Type != LogicLamp) return; continue; }
            int style = gate.FrameY / SourceFramePitch;
            if (style is < 0 or > 5 || !CanFrameLogic(x, y)) return;
            int count = 0, on = 0;
            bool faulty = false;
            for (int above = y - 1; above > 0; above--)
            {
                var cell = _tiles.Get(x, above);
                if (!cell.IsActive || cell.Type != LogicLamp) break;
                if (cell.FrameX == SwitchFrameWidth) { faulty = true; break; }
                count++;
                if (cell.FrameX == SourceFramePitch) on++;
            }
            bool output = style switch { 0 => count == on, 1 => on > 0, 2 => count != on,
                3 => on == 0, 4 => on == 1, _ => on != 1 };
            bool repair = !faulty && gate.FrameX == SwitchFrameWidth;
            bool faultyTriggered = faulty && _tiles.Get(x, originY).FrameX == SwitchFrameWidth;
            if (output == (gate.FrameX == SourceFramePitch) && !repair && !faultyTriggered) return;
            gate.FrameX = faulty ? SwitchFrameWidth : output ? SourceFramePitch : (short)0;
            _tiles.Set(x, y, in gate);
            SkipWire(x, y);
            _publish?.Invoke(x, y, 1, 1);
            // Publication can run host observers; an old gate image cannot authorize
            // random draws or a pulse after an observer replaces that cell.
            if (!_tiles.Get(x, y).Equals(gate)) return;
            bool fire = !faulty || faultyTriggered;
            if (faultyTriggered) fire = _nextLogicSample!(count) < on; // Next(0) still consumes its source sample.
            if (repair || !fire) return;
            int key = Index(x, y);
            if (_gatesDone.Contains(key)) _logicSmoke!(x * 16 - 10, y * 16 - 10);
            else if (SpendLogicEvent()) _gatesNext.Enqueue(key);
            return;
        }
    }

    private bool CanFrameLogic(int x, int y)
    {
        if (!ContainsMechanism(x, y)) return false;
        // CheckLogicTiles/SquareTileFrame is inert for intact dry logic neighborhoods.
        // Other neighbors need the full existing framing/drop transaction before admission.
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            var tile = _tiles.Get(x + dx, y + dy);
            if (tile.LiquidAmount != 0) return false;
            if (!tile.IsActive) continue;
            if (tile.Type is not (LogicLamp or LogicGate) || tile.FrameX is not (0 or SourceFramePitch or SwitchFrameWidth) ||
                tile.FrameY < 0 || tile.FrameY % SourceFramePitch != 0) return false;
            if (tile.Type == LogicGate && tile.FrameY > 5 * SourceFramePitch) return false;
            if (tile.Type == LogicLamp)
            {
                if (y + dy + 1 >= _tiles.Dimensions.HeightTiles || tile.FrameY != 0) return false;
                var support = _tiles.Get(x + dx, y + dy + 1);
                if (!support.IsActive || support.Type is not (LogicLamp or LogicGate)) return false;
            }
        }
        return true;
    }

    private void ClearLogicQueues()
    {
        _logicLamps.Clear(); _gatesCurrent.Clear(); _gatesNext.Clear(); _gatesDone.Clear();
    }
}
