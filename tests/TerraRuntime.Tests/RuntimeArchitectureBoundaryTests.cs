using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Contracts.Runtime;
using System.Reflection;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;

namespace TerraRuntime.Tests;

public sealed class RuntimeArchitectureBoundaryTests
{
    private static readonly string[] ProductionRoots =
    [
        "TerraRuntime.Server",
        "TerraRuntime.Extensible.Server",
        "TerraRuntime.Application",
        "TerraRuntime.Extensibility",
        "TerraRuntime.Contracts",
        "TerraRuntime.Core",
        "TerraRuntime.Gameplay",
        "TerraRuntime.HostContracts",
        "TerraRuntime.Network",
        "TerraRuntime.Protocol",
        "TerraRuntime.Protocol.Multiplicity",
        "TerraRuntime.World"
    ];

    private static readonly string[] ForbiddenRuntimeDependencyPrefixes =
    [
        "Terraria",
        "TShock",
        "OTAPI",
        "Vega"
    ];

    private static readonly IReadOnlyDictionary<string, HashSet<string>> AllowedFoundationReferences =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["TerraRuntime.Contracts"] = [],
            ["TerraRuntime.Gameplay"] = ["TerraRuntime.Contracts"],
            ["TerraRuntime.Core"] = ["TerraRuntime.Contracts", "TerraRuntime.Gameplay"],
            ["TerraRuntime.HostContracts"] = ["TerraRuntime.Contracts"],
            ["TerraRuntime.Protocol"] = [],
            ["TerraRuntime.World"] = ["TerraRuntime.Contracts"],
            ["TerraRuntime.Network"] = ["TerraRuntime.Contracts", "TerraRuntime.Protocol"],
            ["TerraRuntime.Protocol.Multiplicity"] =
            [
                "TerraRuntime.Contracts",
                "TerraRuntime.Protocol",
                "TerraRuntime.World"
            ]
        };

    [Fact]
    public void Production_assembly_closure_does_not_reference_external_game_server_runtimes()
    {
        foreach (Assembly assembly in LoadProductionClosure())
        {
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                string referenceName = reference.Name ?? string.Empty;
                string? forbiddenPrefix = ForbiddenRuntimeDependencyPrefixes.FirstOrDefault(
                    prefix => referenceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

                Assert.True(
                    forbiddenPrefix is null,
                    $"{assembly.GetName().Name} must not reference {referenceName}; " +
                    $"runtime dependencies beginning with '{forbiddenPrefix}' are outside the TerraRuntime boundary.");
            }
        }
    }

    [Fact]
    public void Multiplicity_is_visible_only_to_the_protocol_adapter()
    {
        foreach (Assembly assembly in LoadProductionClosure())
        {
            string assemblyName = assembly.GetName().Name ?? string.Empty;
            bool referencesMultiplicity = assembly.GetReferencedAssemblies().Any(
                reference => (reference.Name ?? string.Empty).StartsWith("Multiplicity", StringComparison.Ordinal));

            if (assemblyName == "TerraRuntime.Protocol.Multiplicity")
            {
                Assert.True(
                    referencesMultiplicity,
                    "The Multiplicity adapter must retain the package dependency it is responsible for isolating.");
                continue;
            }

            Assert.False(
                referencesMultiplicity,
                $"{assemblyName} references Multiplicity directly instead of using TerraRuntime.Protocol.Multiplicity.");
        }
    }

    [Fact]
    public void Shipping_launchers_depend_on_the_shared_application_composition_not_each_other()
    {
        Assembly standalone = Assembly.Load(new AssemblyName("TerraRuntime.Server"));
        Assembly extensible = Assembly.Load(new AssemblyName("TerraRuntime.Extensible.Server"));

        Assert.Equal(
            ["TerraRuntime.Application"],
            GetTerraRuntimeReferences(standalone));
        Assert.Equal(
            ["TerraRuntime.Extensibility"],
            GetTerraRuntimeReferences(extensible));

        Assert.DoesNotContain(
            extensible.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "TerraRuntime.Server", StringComparison.Ordinal));

        Assembly extensibility = Assembly.Load(new AssemblyName("TerraRuntime.Extensibility"));
        Assert.Equal(
            ["TerraRuntime.Application", "TerraRuntime.Contracts", "TerraRuntime.Core", "TerraRuntime.HostContracts"],
            GetTerraRuntimeReferences(extensibility));
        Assert.DoesNotContain(
            extensibility.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "TerraRuntime.Server", StringComparison.Ordinal));

        Assembly application = Assembly.Load(new AssemblyName("TerraRuntime.Application"));
        Assert.DoesNotContain(
            application.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "TerraRuntime.Extensibility", StringComparison.Ordinal));
        Assert.DoesNotContain(
            application.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, "TerraRuntime.Extensible.Server", StringComparison.Ordinal));
    }

    [Fact]
    public void Foundation_layers_do_not_grow_undeclared_production_dependencies()
    {
        IReadOnlyDictionary<string, Assembly> closure = LoadProductionClosure()
            .ToDictionary(assembly => assembly.GetName().Name!, StringComparer.Ordinal);

        foreach ((string assemblyName, HashSet<string> allowed) in AllowedFoundationReferences)
        {
            Assert.True(closure.TryGetValue(assemblyName, out Assembly? assembly), $"Missing production assembly {assemblyName}.");

            string[] actualProductionReferences = assembly!
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name is not null && name.StartsWith("TerraRuntime", StringComparison.Ordinal))
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            foreach (string reference in actualProductionReferences)
            {
                Assert.True(
                    allowed.Contains(reference),
                    $"{assemblyName} gained undeclared production dependency {reference}. " +
                    "Update the architecture deliberately before changing this allow-set.");
            }
        }
    }

    [Fact]
    public void Town_content_rules_live_in_gameplay_while_mutable_spawn_cadence_remains_in_core()
    {
        Type[] gameplayTypes =
        [
            typeof(VanillaTownShopCatalog1458),
            typeof(VanillaSpecialTownShopCatalog1458),
            typeof(VanillaTownHappiness1458),
            typeof(VanillaTownNpcSpawnEligibility1458),
            typeof(VanillaTownNpcSpawnItemFacts1458),
            typeof(VanillaTownNpcRescue1458),
            typeof(VanillaTownNpcIdentityResolver1458),
            typeof(VanillaNpcSpawnCadence)
        ];

        foreach (Type gameplayType in gameplayTypes)
        {
            Assert.Equal("TerraRuntime.Gameplay", gameplayType.Assembly.GetName().Name);
            Assert.Equal("TerraRuntime.Gameplay.Npcs", gameplayType.Namespace);
        }

        Assert.Equal("TerraRuntime.Contracts", typeof(VanillaMoonPhase).Assembly.GetName().Name);
        Assert.Equal("TerraRuntime.Contracts.Gameplay", typeof(VanillaMoonPhase).Namespace);
        Assert.Equal("TerraRuntime.Core", typeof(VanillaTownNpcSpawnCadence1458).Assembly.GetName().Name);
    }

    [Fact]
    public void Player_contracts_rules_and_runtime_ownership_follow_the_dependency_layers()
    {
        Type[] contractTypes =
        [
            typeof(PlayerAppearanceCommitRequest),
            typeof(PlayerEquipmentCommitRequest),
            typeof(PlayerMovementCommitRequest),
            typeof(PlayerSpawnCommitRequest),
            typeof(PlayerHealthCommitRequest),
            typeof(PlayerManaCommitRequest)
        ];

        foreach (Type contractType in contractTypes)
        {
            Assert.Equal("TerraRuntime.Contracts", contractType.Assembly.GetName().Name);
            Assert.Equal("TerraRuntime.Contracts.Runtime", contractType.Namespace);
        }

        Type[] gameplayTypes =
        [
            typeof(VanillaPlayerAppearanceNormalizer),
            typeof(VanillaPlayerMovementNormalizer),
            typeof(VanillaPlayerSpawnValidator),
            typeof(VanillaPlayerVitalsRules),
            typeof(VanillaPlayerHitboxFacts)
        ];

        foreach (Type gameplayType in gameplayTypes)
        {
            Assert.Equal("TerraRuntime.Gameplay", gameplayType.Assembly.GetName().Name);
            Assert.Equal("TerraRuntime.Gameplay.Players", gameplayType.Namespace);
        }

        Assert.Equal("TerraRuntime.Core", typeof(IPlayerAppearanceIngress).Assembly.GetName().Name);
        Assert.Equal("TerraRuntime.Core", typeof(ServerPlayerStateStore).Assembly.GetName().Name);
        Assert.Equal("TerraRuntime.Core.Players", typeof(ServerPlayerStateStore).Namespace);
        Assert.Equal("TerraRuntime.Core.Players", typeof(ServerPlayerSlotRegistry).Namespace);
        Assert.Equal("TerraRuntime.Application", typeof(PlayerEquipmentPacket5Normalizer).Assembly.GetName().Name);
    }

    [Fact]
    public void Npc_simulation_loot_and_support_catalogs_live_in_gameplay_while_runtime_execution_stays_in_core()
    {
        Type[] gameplayTypes =
        [
            typeof(VanillaNpcGravity),
            typeof(VanillaNpcTargeting),
            typeof(VanillaNpcKnockbackResolver),
            typeof(VanillaBlueSlimeMotion),
            typeof(VanillaKingSlimeMotion),
            typeof(VanillaWormMotion),
            typeof(VanillaFlyerProjectileAttack),
            typeof(VanillaNpcAiCoverageCatalog),
            typeof(VanillaBrainOfCthulhuLootEvaluator),
            typeof(VanillaEaterOfWorldsLootEvaluator),
            typeof(VanillaQueenBeeLootEvaluator),
            typeof(VanillaSkeletronLootEvaluator),
            typeof(VanillaKingSlimeDifficultyLootEvaluator),
            typeof(NpcLootWorldItemOrigin),
            typeof(VanillaNpcPlayerInteractionFacts)
        ];

        foreach (Type gameplayType in gameplayTypes)
        {
            Assert.Equal("TerraRuntime.Gameplay", gameplayType.Assembly.GetName().Name);
            Assert.Equal("TerraRuntime.Gameplay.Npcs", gameplayType.Namespace);
        }

        Assert.Equal("TerraRuntime.Core", typeof(RuntimeNpcStore).Assembly.GetName().Name);
        Assert.Equal("TerraRuntime.Core", typeof(RuntimeNpcDamageExecutor).Assembly.GetName().Name);
        Assert.Equal("TerraRuntime.Core", typeof(RuntimeNpcLootWorldItemTransaction).Assembly.GetName().Name);
        Assert.Equal("TerraRuntime.Core", typeof(RuntimeKingSlimeDifficultyLootFinalizer).Assembly.GetName().Name);
        Assert.Equal("TerraRuntime.Core", typeof(VanillaTownNpcSpawnCadence1458).Assembly.GetName().Name);
    }

    [Fact]
    public void Extension_dispatch_semantics_live_in_gameplay_while_registry_and_state_ownership_stay_in_core()
    {
        Type[] gameplayTypes =
        [
            typeof(GameplayExtensionRandom),
            typeof(GameplayBehaviorStage),
            typeof(GameplayBehaviorBinding<object>),
            typeof(GameplayBehaviorDispatchPlan<object>)
        ];

        foreach (Type gameplayType in gameplayTypes)
        {
            Assert.Equal("TerraRuntime.Gameplay", gameplayType.Assembly.GetName().Name);
            Assert.Equal("TerraRuntime.Gameplay.Extensions", gameplayType.Namespace);
        }

        Assert.Equal("TerraRuntime.Core", typeof(RuntimeGameplayBehaviorRegistry<,>).Assembly.GetName().Name);
        Assert.Equal("TerraRuntime.Core", typeof(RuntimeNpcExtensionStateStore<>).Assembly.GetName().Name);
        Assert.Equal("TerraRuntime.Core", typeof(RuntimeNpcArchetypeIdentityStore).Assembly.GetName().Name);
    }

    [Fact]
    public void Projectile_identity_gameplay_rules_and_runtime_state_follow_the_dependency_layers()
    {
        Assert.Equal("TerraRuntime.Contracts", typeof(ProjectileTypeId).Assembly.GetName().Name);
        Assert.Equal("TerraRuntime.Contracts.Gameplay", typeof(ProjectileTypeId).Namespace);

        Type[] gameplayTypes =
        [
            typeof(VanillaProjectileDefinitionCatalog),
            typeof(VanillaProjectileLifecycleFacts),
            typeof(VanillaProjectileFacts),
            typeof(VanillaProjectileOwnership),
            typeof(VanillaProjectileUpdateFacts),
            typeof(VanillaProjectileReflection1458)
        ];

        foreach (Type gameplayType in gameplayTypes)
        {
            Assert.Equal("TerraRuntime.Gameplay", gameplayType.Assembly.GetName().Name);
            Assert.Equal("TerraRuntime.Gameplay.Projectiles", gameplayType.Namespace);
        }

        Assert.Equal("TerraRuntime.Core", typeof(RuntimeProjectileStore).Assembly.GetName().Name);
        Assert.Equal("TerraRuntime.Core", typeof(ProjectileLifecycleState).Assembly.GetName().Name);
        Assert.Equal("TerraRuntime.Core", typeof(RuntimeProjectileStateExecutor).Assembly.GetName().Name);
        Assert.Equal("TerraRuntime.Core", typeof(ProjectileNpcHitIntentBuilder).Assembly.GetName().Name);
    }

    [Fact]
    public void Host_contract_public_surface_does_not_expose_concrete_runtime_assemblies()
    {
        Assembly hostContracts = Assembly.Load(new AssemblyName("TerraRuntime.HostContracts"));

        foreach (Type exportedType in hostContracts.GetExportedTypes())
        {
            foreach (Type signatureType in EnumeratePublicSignatureTypes(exportedType))
            {
                foreach (Type expanded in ExpandType(signatureType))
                {
                    string dependency = expanded.Assembly.GetName().Name ?? string.Empty;
                    if (!dependency.StartsWith("TerraRuntime", StringComparison.Ordinal))
                        continue;

                    Assert.True(
                        dependency is "TerraRuntime.HostContracts" or "TerraRuntime.Contracts",
                        $"Public host contract {exportedType.FullName} exposes {expanded.FullName} from concrete runtime assembly {dependency}.");
                }
            }
        }
    }

    private static string[] GetTerraRuntimeReferences(Assembly assembly) =>
        assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name.StartsWith("TerraRuntime", StringComparison.Ordinal))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<Assembly> LoadProductionClosure()
    {
        var loaded = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pending = new Queue<AssemblyName>(ProductionRoots.Select(name => new AssemblyName(name)));

        while (pending.Count > 0)
        {
            AssemblyName requested = pending.Dequeue();
            string requestedName = requested.Name ?? string.Empty;
            if (loaded.ContainsKey(requestedName))
                continue;

            Assembly assembly = Assembly.Load(requested);
            string assemblyName = assembly.GetName().Name ?? requestedName;
            if (!assemblyName.StartsWith("TerraRuntime", StringComparison.Ordinal))
                continue;

            loaded.Add(assemblyName, assembly);
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                if ((reference.Name ?? string.Empty).StartsWith("TerraRuntime", StringComparison.Ordinal) &&
                    !loaded.ContainsKey(reference.Name!))
                {
                    pending.Enqueue(reference);
                }
            }
        }

        return loaded.Values.OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<Type> EnumeratePublicSignatureTypes(Type exportedType)
    {
        if (exportedType.BaseType is not null)
            yield return exportedType.BaseType;

        foreach (Type contract in exportedType.GetInterfaces())
            yield return contract;

        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (ConstructorInfo constructor in exportedType.GetConstructors(Flags))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
                yield return parameter.ParameterType;
        }

        foreach (MethodInfo method in exportedType.GetMethods(Flags))
        {
            yield return method.ReturnType;
            foreach (ParameterInfo parameter in method.GetParameters())
                yield return parameter.ParameterType;
        }

        foreach (PropertyInfo property in exportedType.GetProperties(Flags))
        {
            yield return property.PropertyType;
            foreach (ParameterInfo parameter in property.GetIndexParameters())
                yield return parameter.ParameterType;
        }

        foreach (FieldInfo field in exportedType.GetFields(Flags))
            yield return field.FieldType;

        foreach (EventInfo @event in exportedType.GetEvents(Flags))
        {
            if (@event.EventHandlerType is not null)
                yield return @event.EventHandlerType;
        }
    }

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is Type elementType)
        {
            foreach (Type nested in ExpandType(elementType))
                yield return nested;
        }

        if (!type.IsGenericType)
            yield break;

        foreach (Type argument in type.GetGenericArguments())
        {
            foreach (Type nested in ExpandType(argument))
                yield return nested;
        }
    }
}
