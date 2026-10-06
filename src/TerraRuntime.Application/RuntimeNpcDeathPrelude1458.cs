using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Npcs.Loot;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
namespace TerraRuntime.Application;

internal readonly record struct RuntimeNpcDeathPreludeContext1458(bool HasInteractions, bool TerminalEater,
    bool TwinPeerAlive, bool Hardmode, bool GoodWorld, bool IsThereWorldSurface, bool SkeletronDowned,
    bool? OnlyShimmerOceanWorlds, ReadOnlyMemory<PlayerHandle> AchievementRecipients,
    int BannerPlayerSlot = -1, string? BannerPlayerName = null, bool HasOwnInteractions = false);

/// <summary>One world-loop owner for source death credit and claimable banners. Preview has no external callbacks.</summary>
internal sealed class RuntimeNpcDeathPrelude1458
{
    private readonly int[] kills = new int[VanillaNpcDeathPreludeCatalog1458.BannerCount];
    private readonly ushort[] claims = new ushort[VanillaNpcDeathPreludeCatalog1458.BannerCount];
    private readonly Dictionary<string, int> bestiary = new(StringComparer.Ordinal);
    private readonly string[] sightings;
    private readonly string[] chats;
    private readonly RuntimeNpcReplicationRegistry? replication;
    private readonly List<(PlayerHandle? Player, byte[] Frame)> pendingFrames = [];
    private readonly int maximumBestiaryKeys;
    private bool applied;
    private ReadOnlyMemory<byte>[] joinFrames = [];
    internal ReadOnlyMemory<byte>[] CaptureJoinFrames() => Volatile.Read(ref joinFrames);
    public ulong Revision { get; private set; } = 1;

    public RuntimeNpcDeathPrelude1458(WorldBannerData1458? banners = null, WorldBestiaryData? bestiary = null,
        RuntimeNpcReplicationRegistry? replication = null)
    {
        this.replication = replication;
        banners ??= WorldBannerData1458.Empty;
        if (!banners.IsValid) throw new InvalidDataException("Unsupported banner state.");
        banners.KillCounts.CopyTo(kills, 0); banners.ClaimableCounts.CopyTo(claims, 0);
        bestiary ??= new([], [], []);
        if (bestiary.Kills.Length > 1000000) throw new InvalidDataException("Bestiary entry budget exceeded.");
        // Match the checkpoint encoder's admitted entry budget. A full imported ledger rejects a new key
        // in the death preview before live damage, rather than accepting an unsavable ledger.
        maximumBestiaryKeys = 1000000;
        foreach (WorldBestiaryKill entry in bestiary.Kills)
            if (string.IsNullOrEmpty(entry.PersistentId) || entry.KillCount is < 0 or > VanillaNpcDeathPreludeCatalog1458.MaximumBestiaryKills ||
                !this.bestiary.TryAdd(entry.PersistentId, entry.KillCount)) throw new InvalidDataException("Invalid bestiary baseline.");
        sightings = (string[])bestiary.Sightings.Clone(); chats = (string[])bestiary.Chats.Clone();
        PublishJoinFrames();
    }
    public RuntimeNpcDeathPrelude1458 CreatePreview()
    {
        var result = new RuntimeNpcDeathPrelude1458(CaptureBanners(), CaptureBestiary());
        result.Revision = Revision; return result;
    }
    public WorldBannerData1458 CaptureBanners() => new((int[])kills.Clone(), (ushort[])claims.Clone());
    public WorldBestiaryData CaptureBestiary() => new(bestiary.Select(static entry => new WorldBestiaryKill(entry.Key, entry.Value)).ToArray(),
        (string[])sightings.Clone(), (string[])chats.Clone());

    // WorldItem.VoodooDollLavaDeath registers this kill before StrikeNPC, without an interaction gate.
    internal bool TryRegisterScriptedKill(in NpcSnapshot victim)
    {
        if (applied || Revision == ulong.MaxValue ||
            !VanillaNpcDeathPreludeCatalog1458.TryGet(victim.TypeIdentity, victim.NetIdentity, out var facts)) return false;
        applied = true;
        if (VanillaNpcDeathPreludeCatalog1458.IsExcludedBestiaryType(victim.TypeIdentity)) return true;
        bestiary.TryGetValue(facts.BestiaryCreditId, out int count);
        if (!bestiary.ContainsKey(facts.BestiaryCreditId) && bestiary.Count == maximumBestiaryKeys) return false;
        int reported = count + 1;
        bestiary[facts.BestiaryCreditId] = Math.Min(reported, VanillaNpcDeathPreludeCatalog1458.MaximumBestiaryKills);
        pendingFrames.Add((null, TerrariaNpcDeathPreludeCodec1458.EncodeBestiaryKill(checked((short)victim.NetId), reported)));
        Revision++;
        return true;
    }

    public bool TryApply(in NpcSnapshot dead, in RuntimeNpcDeathPreludeContext1458 context,
        VanillaUnifiedRandom1458 random, out bool allowLoot)
    {
        ArgumentNullException.ThrowIfNull(random); allowLoot = false;
        if (applied || Revision == ulong.MaxValue || !VanillaNpcDeathPreludeCatalog1458.TryGet(dead.TypeIdentity, dead.NetIdentity, out var facts) ||
            context.AchievementRecipients.Length > VanillaNpcPlayerInteractionFacts.InteractablePlayerSlots) return false;
        foreach (PlayerHandle player in context.AchievementRecipients.Span)
            if (!player.IsAssigned) return false;
        // One preview represents one death. At most 255 targeted achievements and four shared credit frames.
        applied = true;
        if (VanillaNpcDeathPreludeCatalog1458.HasDarkCasterLocalGate(dead.TypeIdentity) && dead.Simulation.LocalAi.Ai3 == 1f) return true;
        if (VanillaNpcDeathPreludeCatalog1458.HasDungeonLootGate(dead.TypeIdentity) &&
            (context.GoodWorld || !context.IsThereWorldSurface) && !context.SkeletronDowned)
        {
            if (!context.OnlyShimmerOceanWorlds.HasValue) return false;
            if (!context.OnlyShimmerOceanWorlds.Value) return true;
        }
        bool eater = VanillaEaterOfWorldsLifecycle.IsSegment(dead.TypeIdentity);
        bool notifyAchievement = !eater || context.TerminalEater;
        if ((dead.TypeIdentity == VanillaNpcIds.Retinazer || dead.TypeIdentity == VanillaNpcIds.Spazmatism) && context.TwinPeerAlive)
            notifyAchievement = false;
        if (notifyAchievement)
            foreach (PlayerHandle player in context.AchievementRecipients.Span)
            {
                pendingFrames.Add((player, TerrariaNpcDeathPreludeCodec1458.EncodeNpcKillAchievement(checked((short)dead.NetId))));
            }
        if (context.HasInteractions)
        {
            if (!VanillaNpcDeathPreludeCatalog1458.IsExcludedBestiaryType(dead.TypeIdentity) && (!eater || context.TerminalEater))
            {
                bestiary.TryGetValue(facts.BestiaryCreditId, out int count);
                if (!bestiary.ContainsKey(facts.BestiaryCreditId) && bestiary.Count == maximumBestiaryKeys) return false;
                int reportedCount = count + 1; // RegisterKill sends the increment before SetKillCountDirectly clamps storage.
                bestiary[facts.BestiaryCreditId] = Math.Min(reportedCount, VanillaNpcDeathPreludeCatalog1458.MaximumBestiaryKills);
                pendingFrames.Add((null, TerrariaNpcDeathPreludeCodec1458.EncodeBestiaryKill(checked((short)dead.NetId), reportedCount)));
            }
            if (!facts.ExcludedFromTally && facts.BannerId > 0)
            {
                short banner = checked((short)facts.BannerId);
                kills[banner] = unchecked(kills[banner] + 1);
                pendingFrames.Add((null, TerrariaNpcDeathPreludeCodec1458.EncodeBannerKill(banner, kills[banner])));
                if (facts.KillsToBanner <= 0) return false;
                if (kills[banner] % facts.KillsToBanner == 0)
                {
                    if (claims[banner] < VanillaNpcDeathPreludeCatalog1458.MaximumClaimableBanners)
                    {
                        claims[banner]++;
                        pendingFrames.Add((null, TerrariaNpcDeathPreludeCodec1458.EncodeBannerClaimCount(banner, claims[banner])));
                    }
                    if (!VanillaBannerCatalog1458.TryGet(banner, out var bannerFacts)) return false;
                    pendingFrames.Add((null, TerrariaBannerAnnouncementCodec1458.Encode(kills[banner], bannerFacts.NameKey,
                        context.BannerPlayerSlot, context.BannerPlayerName)));
                }
            }
        }
        if (Revision == ulong.MaxValue) return false; Revision++;
        if (dead.Simulation.SpawnedFromStatue)
        {
            if (facts.NoEarlyStatueLoot && !context.Hardmode) return true;
            if (facts.StatueDropRarity != -1f && ((float)random.NextDouble() >= facts.StatueDropRarity || !context.HasOwnInteractions)) return true;
        }
        allowLoot = true; return true;
    }
    public bool TryPublish(RuntimeNpcDeathPrelude1458 preview, ulong expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (Revision != expectedRevision || preview.Revision < Revision) return false;
        preview.kills.CopyTo(kills, 0); preview.claims.CopyTo(claims, 0);
        bestiary.Clear(); foreach (var entry in preview.bestiary) bestiary.Add(entry.Key, entry.Value);
        Revision = preview.Revision;
        PublishJoinFrames();
        foreach (var entry in preview.pendingFrames)
            if (entry.Player is PlayerHandle player) replication?.TrySendDeathPrelude(player, entry.Frame);
            else replication?.BroadcastDeathPrelude(entry.Frame);
        return true;
    }
    public bool TryClaim(ConnectionHandle connection, in TerrariaBannerClaim1458 request)
    {
        if ((uint)request.Banner >= claims.Length || replication?.IsDeathPreludePlayerCurrent(connection) != true || Revision == ulong.MaxValue) return false;
        ushort requested = request.Amount == 0 ? (ushort)1 : request.Amount;
        ushort granted = Math.Min(requested, claims[request.Banner]);
        if (granted > 0)
        {
            claims[request.Banner] -= granted; Revision++; PublishJoinFrames();
            replication.BroadcastDeathPrelude(TerrariaNpcDeathPreludeCodec1458.EncodeBannerClaimCount(request.Banner, claims[request.Banner]));
            replication.TrySendDeathPrelude(connection.Player, TerrariaNpcDeathPreludeCodec1458.EncodeClaimResponse(request.Banner, granted, true));
        }
        ushort remainder = checked((ushort)(requested - granted));
        if (remainder > 0) replication.TrySendDeathPrelude(connection.Player, TerrariaNpcDeathPreludeCodec1458.EncodeClaimResponse(request.Banner, remainder, false));
        return true;
    }

    internal bool TryAdoptUnpublished(RuntimeNpcDeathPrelude1458 preview, ulong expectedRevision,
        out PreludePublication? publication)
    {
        publication = null;
        if (ReferenceEquals(preview, this) || Revision != expectedRevision || preview.Revision < Revision)
            return false;
        var frames = preview.pendingFrames.ToArray();
        preview.kills.CopyTo(kills, 0);
        preview.claims.CopyTo(claims, 0);
        bestiary.Clear();
        foreach (var entry in preview.bestiary) bestiary.Add(entry.Key, entry.Value);
        Revision = preview.Revision;
        PublishJoinFrames();
        publication = new(this, Revision, frames);
        return true;
    }

    internal sealed class PreludePublication(RuntimeNpcDeathPrelude1458 owner, ulong revision,
        (PlayerHandle? Player, byte[] Frame)[] frames)
    {
        private int next;
        private bool published;

        internal bool TryPublish()
        {
            if (published) return false;
            published = true;
            while (next < frames.Length)
            {
                if (owner.Revision != revision) break;
                var entry = frames[next++];
                if (entry.Player is PlayerHandle player)
                    owner.replication?.TrySendDeathPrelude(player, entry.Frame);
                else owner.replication?.BroadcastDeathPrelude(entry.Frame);
            }
            return true;
        }
    }

    private void PublishJoinFrames()
    {
        // Immutable published bytes are the sole cross-thread view: no connection thread reads the ledgers.
        var frames = new List<ReadOnlyMemory<byte>>(763)
        { TerrariaNpcDeathPreludeCodec1458.EncodeBannerFull(kills, claims) };
        foreach (var entry in bestiary)
            if (VanillaNpcBestiaryNetCatalog1458.TryGetNetId(entry.Key, out short netId))
                frames.Add(TerrariaNpcDeathPreludeCodec1458.EncodeBestiaryKill(netId, entry.Value));
        foreach (string id in sightings.Distinct(StringComparer.Ordinal))
            if (VanillaNpcBestiaryNetCatalog1458.TryGetNetId(id, out short netId))
                frames.Add(TerrariaNpcDeathPreludeCodec1458.EncodeBestiarySight(netId));
        foreach (string id in chats.Distinct(StringComparer.Ordinal))
            if (VanillaNpcBestiaryNetCatalog1458.TryGetNetId(id, out short netId))
                frames.Add(TerrariaNpcDeathPreludeCodec1458.EncodeBestiaryChat(netId));
        Volatile.Write(ref joinFrames, frames.ToArray());
    }
}
