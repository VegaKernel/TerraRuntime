using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Buffs;

namespace TerraRuntime.Application;

// One player's bounded authoritative buff slots. Network snapshots supply presence and reset durations;
// they are not independently counted down for remote players by Terraria's dedicated server.
internal sealed class PlayerBuffState
{
    public const int Capacity = 44;
    private readonly BuffTypeId[] types = new BuffTypeId[Capacity];
    private readonly int[] durations = new int[Capacity];
    private int count;

    public BuffTypeId[] CaptureTypes() => types.AsSpan(0, count).ToArray();

    public void ReplaceNetworkSnapshot(ReadOnlySpan<BuffTypeId> snapshot)
    {
        if (snapshot.Length > Capacity) throw new ArgumentOutOfRangeException(nameof(snapshot));
        foreach (BuffTypeId type in snapshot)
            if (type == VanillaBuffIds.None || !VanillaBuffIds.TryCreate(type.Value, out _))
                throw new ArgumentException("Invalid player buff identity.", nameof(snapshot));
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
        if (immune || duration <= 0) return false;
        int existing = Array.IndexOf(types, VanillaBuffIds.MoonLeech, 0, count);
        if (existing >= 0)
        {
            durations[existing] = Math.Max(durations[existing], duration);
            return true;
        }
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
            types[insert] = VanillaBuffIds.MoonLeech;
            durations[insert] = duration;
            return true;
        }
        types[count] = VanillaBuffIds.MoonLeech;
        durations[count++] = duration;
        return true;
    }
}
