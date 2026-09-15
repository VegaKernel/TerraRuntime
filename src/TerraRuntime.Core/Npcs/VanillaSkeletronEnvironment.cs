namespace TerraRuntime.Core.Npcs;

/// <summary>Read-only world query for the bounded AI_011 caster summon search, TerrariaServer 1.4.5.8.</summary>
public interface IVanillaSkeletronEnvironment
{
    bool TryFindCasterSpawn(float centerX, float centerY, IVanillaNpcRandom random, out int bottomX, out int bottomY);
}
