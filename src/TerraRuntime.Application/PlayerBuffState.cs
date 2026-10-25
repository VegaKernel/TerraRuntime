using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Buffs;

namespace TerraRuntime.Application;

// One player's bounded authoritative buff slots. Network snapshots supply presence and reset durations;
// they are not independently counted down for remote players by Terraria's dedicated server.
internal sealed partial class PlayerBuffState
{
    public const int Capacity = 44;
    private readonly BuffTypeId[] types = new BuffTypeId[Capacity];
    private readonly int[] durations = new int[Capacity];
    private int count;
    private readonly bool[] ownedFood = new bool[Capacity];
    // Clone-carried semantic identity: only the owned clock may preserve it across changes.
    private object foodClockLineage = new();
    private readonly byte[] foodClockOrigins = Enumerable.Range(0, Capacity).Select(i => (byte)i).ToArray();
    private void InvalidateFoodClockLineage()
    {
        foodClockLineage = new();
        for (int i = 0; i < Capacity; i++) foodClockOrigins[i] = (byte)i;
    }
    private long lastOwnedFoodTick = long.MinValue;
    private bool ownedFoodEffectAtTick;
    internal bool HasOwnedFoodSlots => ownedFood.Contains(true);
    internal bool HadOwnedFoodEffect(long tick) => lastOwnedFoodTick == tick ? ownedFoodEffectAtTick : HasOwnedFood;
    internal void ResetOwnedFoodClockForTransfer() { InvalidateFoodClockLineage(); lastOwnedFoodTick = long.MinValue; ownedFoodEffectAtTick = false; }


    internal bool HasOwnedFood => Enumerable.Range(0, count).Any(i => ownedFood[i] && types[i].Value is 26 or 206 or 207 && durations[i] > 0);

    internal BuffTypeId[] CaptureEffectTypes() => Enumerable.Range(0, count)
        .Where(i => types[i] != VanillaBuffIds.None && durations[i] > 0 &&
            (types[i].Value is not (26 or 206 or 207) || ownedFood[i]))
        .Select(i => types[i]).ToArray();

    public BuffTypeId[] CaptureTypes()
    {
        int active = 0;
        for (int i = 0; i < count; i++) if (types[i] != VanillaBuffIds.None && durations[i] > 0) active++;
        var snapshot = new BuffTypeId[active];
        int index = 0;
        for (int i = 0; i < count; i++) if (types[i] != VanillaBuffIds.None && durations[i] > 0) snapshot[index++] = types[i];
        return snapshot;
    }

    public int CountActive(BuffTypeId type)
    {
        int result = 0;
        for (int i = 0; i < count; i++) if (types[i] == type && durations[i] > 0) result++;
        return result;
    }

    internal PlayerDebuffSnapshot1458 CaptureDebuffFlags() =>
        new(CountActive(VanillaBuffIds.OnFire) != 0 || contactDebuffsForUpdate.OnFire,
            CountActive(VanillaBuffIds.CursedInferno) != 0 || contactDebuffsForUpdate.OnFire2,
            CountActive(VanillaBuffIds.Poisoned) != 0 || contactDebuffsForUpdate.Poisoned);

    public bool HasNonPersistentBuffs()
    {
        for (int i = 0; i < count; i++)
            if (types[i] != VanillaBuffIds.None && !VanillaBuffDefinitionCatalog.PersistsThroughPlayerDeath(types[i])) return true;
        return false;
    }

    public bool ClearNonPersistentOnDeath()
    {
        contactDebuffsForUpdate = default;
        contactFrozenForUpdate = false;
        InvalidateFoodClockLineage();
        bool changed = false;
        // UpdateDead clears slots in place; don't move a preserved last slot into an earlier position.
        for (int i = 0; i < count; i++)
            if (types[i] != VanillaBuffIds.None && !VanillaBuffDefinitionCatalog.PersistsThroughPlayerDeath(types[i]))
            { types[i] = VanillaBuffIds.None; durations[i] = 0; ownedFood[i] = false; ownedNpcContact[i] = false; changed = true; }
        return changed;
    }

    public void ReplaceNetworkSnapshot(ReadOnlySpan<BuffTypeId> snapshot)
    {
        if (snapshot.Length > Capacity) throw new ArgumentOutOfRangeException(nameof(snapshot));
        foreach (BuffTypeId type in snapshot)
            if (type == VanillaBuffIds.None || !VanillaBuffIds.TryCreate(type.Value, out _))
                throw new ArgumentException("Invalid player buff identity.", nameof(snapshot));
        if (HasOwnedFood && snapshot.SequenceEqual(CaptureTypes())) return;
        InvalidateFoodClockLineage();
        ownedFood.AsSpan().Clear();
        ownedNpcContact.AsSpan().Clear();
        snapshot.CopyTo(types);
        types.AsSpan(snapshot.Length).Clear();
        durations.AsSpan(0, snapshot.Length).Fill(60);
        durations.AsSpan(snapshot.Length).Clear();
        count = snapshot.Length;
    }

    public bool Contains(BuffTypeId type, bool immune = false)
    {
        if (immune) return false;
        for (int i = 0; i < count; i++)
            if (types[i] == type && durations[i] >= 1) return true;
        return false;
    }

    public int GetDuration(BuffTypeId type)
    {
        int index = Array.IndexOf(types, type, 0, count);
        return index < 0 ? 0 : durations[index];
    }

    public bool TryApplyMoonLeech(int duration, bool immune = false)
    {
        bool changed = TryApplyMoonLeechCore(duration, immune);
        if (changed) InvalidateFoodClockLineage();
        return changed;
    }

    private bool TryApplyMoonLeechCore(int duration, bool immune)
    {
        if (immune || duration <= 0) return false;
        int existing = Array.IndexOf(types, VanillaBuffIds.MoonLeech, 0, count);
        if (existing >= 0)
        {
            durations[existing] = Math.Max(durations[existing], duration);
            return true;
        }
        for (int i = 0; i < count; i++)
            if (types[i] == VanillaBuffIds.None || durations[i] <= 0)
            { types[i] = VanillaBuffIds.MoonLeech; durations[i] = duration; ownedFood[i] = false; ownedNpcContact[i] = false; return true; }
        if (count == Capacity)
        {
            int replace = 0;
            while (replace < count && VanillaBuffDefinitionCatalog.IsDebuff(types[replace])) replace++;
            if (replace == count) return false;
            // Player.DelBuff compacts only indices 0..42. The final slot remains in place;
            // removing an earlier entry therefore inserts the new buff at 42, before slot 43.
            int insert = replace == Capacity - 1 ? replace : Capacity - 2;
            int shifted = insert - replace;
            Array.Copy(types, replace + 1, types, replace, shifted);
            Array.Copy(durations, replace + 1, durations, replace, shifted);
            Array.Copy(ownedFood, replace + 1, ownedFood, replace, shifted);
            Array.Copy(ownedNpcContact, replace + 1, ownedNpcContact, replace, shifted);
            types[insert] = VanillaBuffIds.MoonLeech;
            durations[insert] = duration;
            ownedFood[insert] = false; ownedNpcContact[insert] = false;
            return true;
        }
        types[count] = VanillaBuffIds.MoonLeech;
        ownedFood[count] = false; ownedNpcContact[count] = false;
        durations[count++] = duration;
        return true;
    }
}
