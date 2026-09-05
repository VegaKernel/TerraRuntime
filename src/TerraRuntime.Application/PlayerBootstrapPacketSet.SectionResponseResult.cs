using TerraRuntime.World;

namespace TerraRuntime.Application;

internal enum PlayerBootstrapSectionResponseResult : byte
{
    Created = 0,
    Unavailable = 1,
    RateLimited = 2
}

public sealed partial class PlayerBootstrapPacketSet
{
    internal PlayerBootstrapSectionResponseResult CreateSectionResponseDetailed(
        int tileX,
        int tileY,
        byte team,
        out PlayerBootstrapSectionResponse response)
    {
        SectionFrameLookupResult baseResult = ResolveBaseSectionFramesDetailed(out ReadOnlyMemory<byte>[] baseSectionFrames);
        if (baseResult != SectionFrameLookupResult.Available)
        {
            response = default;
            return ToSectionResponseResult(baseResult);
        }

        if (_world is null)
        {
            response = new PlayerBootstrapSectionResponse(StatusFrame, baseSectionFrames, []);
            return PlayerBootstrapSectionResponseResult.Created;
        }

        Span<WorldSectionId> requestedSections = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumRequestedSectionCount];
        int requestedCount = InitialSectionBootstrapPlanner.PlanRequestedSections(
            _world.Header.Dimensions,
            tileX,
            tileY,
            requestedSections);

        Span<WorldSectionId> teamSections = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumTeamSpawnSectionCount];
        int teamCount = 0;
        if (_world.RuntimeMetadata.TeamBasedSpawnsSeed &&
            team != 0 &&
            team < _world.RuntimeMetadata.ExtraSpawnPoints.Length)
        {
            var teamSpawn = _world.RuntimeMetadata.ExtraSpawnPoints[team];
            teamCount = InitialSectionBootstrapPlanner.PlanTeamSpawnSections(
                _world.Header.Dimensions,
                teamSpawn.X,
                teamSpawn.Y,
                teamSections);
        }

        if (requestedCount + teamCount == 0)
        {
            response = new PlayerBootstrapSectionResponse(StatusFrame, baseSectionFrames, []);
            return PlayerBootstrapSectionResponseResult.Created;
        }

        Span<WorldSectionId> additionalSections = stackalloc WorldSectionId[
            InitialSectionBootstrapPlanner.MaximumRequestedSectionCount +
            InitialSectionBootstrapPlanner.MaximumTeamSpawnSectionCount];
        int additionalCount = 0;
        AppendUniqueAdditionalSections(requestedSections[..requestedCount], additionalSections, ref additionalCount);
        AppendUniqueAdditionalSections(teamSections[..teamCount], additionalSections, ref additionalCount);

        var additionalFrames = new List<ReadOnlyMemory<byte>>(additionalCount);
        for (int i = 0; i < additionalCount; i++)
        {
            SectionFrameLookupResult lookup = ResolveSectionFrame(
                additionalSections[i],
                out ReadOnlyMemory<byte> sectionFrame);
            if (lookup != SectionFrameLookupResult.Available)
            {
                response = default;
                return ToSectionResponseResult(lookup);
            }

            additionalFrames.Add(sectionFrame);
        }

        ReadOnlyMemory<byte> statusFrame = additionalCount == 0
            ? StatusFrame
            : TerraRuntime.Protocol.Multiplicity.PlayerJoinFrameEncoder.EncodeStatus(
                checked(baseSectionFrames.Length + additionalCount));
        response = new PlayerBootstrapSectionResponse(
            statusFrame,
            baseSectionFrames,
            additionalFrames.ToArray());
        return PlayerBootstrapSectionResponseResult.Created;
    }

    private SectionFrameLookupResult ResolveBaseSectionFramesDetailed(
        out ReadOnlyMemory<byte>[] frames)
    {
        if (_world is null)
        {
            frames = new ReadOnlyMemory<byte>[BaseSectionFrames.Count];
            for (int i = 0; i < frames.Length; i++)
                frames[i] = BaseSectionFrames[i];
            return SectionFrameLookupResult.Available;
        }

        frames = new ReadOnlyMemory<byte>[_baseSections.Length];
        for (int i = 0; i < _baseSections.Length; i++)
        {
            SectionFrameLookupResult lookup = ResolveSectionFrame(
                _baseSections[i],
                out ReadOnlyMemory<byte> frame);
            if (lookup != SectionFrameLookupResult.Available)
            {
                frames = [];
                return lookup;
            }

            frames[i] = frame;
        }

        return SectionFrameLookupResult.Available;
    }

    private static PlayerBootstrapSectionResponseResult ToSectionResponseResult(SectionFrameLookupResult result) =>
        result switch
        {
            SectionFrameLookupResult.Available => PlayerBootstrapSectionResponseResult.Created,
            SectionFrameLookupResult.RateLimited => PlayerBootstrapSectionResponseResult.RateLimited,
            _ => PlayerBootstrapSectionResponseResult.Unavailable
        };
}
