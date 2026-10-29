using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

namespace SourceProbe;

internal static partial class ScalarVerification
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    internal static object Run(ScalarPlan plan, string outputDirectory)
    {
        string runtimeReference = typeof(object).Assembly.Location;
        string runtimeReferenceSha256 = Sha(File.ReadAllBytes(runtimeReference));
        // Unlike source analyze, this explicit offline operation executes the
        // admitted original reference and cache-only generated candidates.
        string original = Path.GetFullPath(plan.Request.Analysis.Assembly);
        if (Sha(File.ReadAllBytes(original)) != "4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034" ||
            Sha(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(original)!, "FNA.dll"))) !=
                "46d201f59ce6c3827414b2c2c5141c218acabda605b02e564d73ca2efb2a3171")
            throw new InvalidDataException("Scalar execution requires the admitted original1458/FNA identities");
        var originalContext = new AssemblyLoadContext("ScalarOriginal", isCollectible: true);
        Assembly? game = null;
        originalContext.Resolving += (_, name) =>
        {
            string path = Path.Combine(Path.GetDirectoryName(original)!, name.Name + ".dll");
            if (File.Exists(path)) return originalContext.LoadFromAssemblyPath(path);
            string? resource = game?.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith("." + name.Name + ".dll", StringComparison.OrdinalIgnoreCase));
            if (resource is null) return null;
            using var stream = game!.GetManifestResourceStream(resource)!;
            return originalContext.LoadFromStream(stream);
        };
        game = originalContext.LoadFromAssemblyPath(original);
        game.GetType("Terraria.Program", true)!.GetField("SavePath", Flags)!.SetValue(null, outputDirectory);
        Type originalType = game.GetType(plan.Method.ContainingType.ToDisplayString(), throwOnError: true)!;
        var originalMethod = originalType.GetMethod(plan.Method.Name, Flags, plan.Method.Parameters.Select(p =>
            p.Type.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Int32 ? typeof(int) : typeof(bool)).ToArray())
            ?? throw new InvalidDataException("Original exact runtime method absent");
        var originalFields = plan.Fields.ToDictionary(p => p.Value.Target,
            p => originalType.GetField(p.Key.Name, Flags) ?? throw new InvalidDataException("Original field absent"));
        if (originalMethod.IsStatic || originalMethod.IsVirtual || originalMethod.DeclaringType != originalType ||
            originalMethod.ReturnType != typeof(void) || originalMethod.GetParameters().Any(p => p.ParameterType.IsByRef) ||
            originalFields.Any(p => p.Value.IsStatic || p.Value.DeclaringType != originalType || p.Value.FieldType !=
                (plan.Fields.Values.Single(f => f.Target == p.Key).Type == "int" ? typeof(int) : typeof(bool))))
            throw new InvalidDataException("Runtime source shape differs");
        Type main = game.GetType("Terraria.Main", true)!;
        main.GetField("dedServ", Flags)!.SetValue(null, true);
        main.GetField("netMode", Flags)!.SetValue(null, 2);
        Type randomType = game.GetType("Terraria.Utilities.UnifiedRandom", true)!;
        FieldInfo randomOwner = main.GetField("rand", Flags)!;
        FieldInfo cursorField = randomType.GetField("inext", Flags)!;
        FieldInfo slotsField = randomType.GetField("SeedArray", Flags)!;
        object Rng(object random)
        {
            int[] slots = ((int[])slotsField.GetValue(random)!).ToArray();
            if (slots.Length != 56) throw new InvalidDataException("Original physical RNG shape differs");
            return new { Cursor = Convert.ToUInt32(cursorField.GetValue(random)), Slots = slots };
        }
        string Canonical(object value) => JsonSerializer.Serialize(value);

        var observations = new List<object>();
        var expected = new List<Dictionary<string, object>>();
        var states = new List<Dictionary<string, object>>();
        var parameters = new List<object?[]>();
        byte[] binary = ScalarCandidate.Compile(plan.Candidate, plan.Carrier);
        Write("candidate.cs", System.Text.Encoding.UTF8.GetBytes(plan.Candidate));
        Write("original-carrier.cs", System.Text.Encoding.UTF8.GetBytes(plan.Carrier));
        Write("candidate.dll", binary);
        var candidateContext = new AssemblyLoadContext("ScalarGenerated", isCollectible: true);
        using var binaryStream = new MemoryStream(binary);
        Assembly candidate = candidateContext.LoadFromStream(binaryStream);
        Type carrier = candidate.GetType("OriginalMethodCarrier.Carrier", true)!;
        Type generatedState = candidate.GetType("GeneratedCandidate.CandidateState", true)!;
        MethodInfo generatedMethod = candidate.GetType("GeneratedCandidate.Candidate", true)!.GetMethod("Run")!;
        MethodInfo carrierMethod = carrier.GetMethod(plan.Method.Name)!;
        int index = 0;
        foreach (var item in plan.Request.Cases)
        {
            var initial = plan.Fields.Values.ToDictionary(f => f.Target, f => ScalarCandidate.Value(item.State[f.Target], f.Type));
            object?[] args = item.Arguments.Select((value, i) => (object?)ScalarCandidate.Value(value,
                plan.Method.Parameters[i].Type.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Int32 ? "int" : "bool")).ToArray();
            object instance = Activator.CreateInstance(originalType)!;
            foreach (var pair in initial) originalFields[pair.Key].SetValue(instance, pair.Value);
            object random = Activator.CreateInstance(randomType, [item.RngSeed])!;
            randomOwner.SetValue(null, random);
            object before = Rng(random);
            originalMethod.Invoke(instance, args);
            object after = Rng(random);
            if (Canonical(before) != Canonical(after)) throw new InvalidDataException("Unowned original RNG effect observed");
            var actualOriginal = originalFields.ToDictionary(p => p.Key, p => p.Value.GetValue(instance)!);
            object carrierInstance = Activator.CreateInstance(carrier)!;
            foreach (var field in plan.Fields) carrier.GetField(field.Key.Name)!.SetValue(carrierInstance, initial[field.Value.Target]);
            carrierMethod.Invoke(carrierInstance, args.ToArray());
            var actualCarrier = plan.Fields.ToDictionary(p => p.Value.Target, p => carrier.GetField(p.Key.Name)!.GetValue(carrierInstance)!);
            object box = Activator.CreateInstance(generatedState)!;
            foreach (var pair in initial) generatedState.GetField(pair.Key)!.SetValue(box, pair.Value);
            object?[] generatedArgs = new object?[] { box }.Concat(args).ToArray();
            generatedMethod.Invoke(null, generatedArgs);
            var actualCandidate = initial.Keys.ToDictionary(k => k, k => generatedState.GetField(k)!.GetValue(generatedArgs[0])!);
            if (Canonical(actualOriginal) != Canonical(actualCarrier) || Canonical(actualOriginal) != Canonical(actualCandidate))
                throw new InvalidDataException("Actual original/carrier/generated scalar states differ at case " + index);
            observations.Add(new { Index = index++, item.RngSeed, Initial = initial, Arguments = args,
                Original = actualOriginal, Carrier = actualCarrier, Candidate = actualCandidate, RngBefore = before, RngAfter = after });
            expected.Add(actualOriginal); states.Add(initial); parameters.Add(args);
        }
        var mutants = new List<object>();
        foreach (var map in plan.Fields.Values)
        {
            string code = ScalarCandidate.OmitWrite(plan, map.Target);
            if (code == plan.Candidate) throw new InvalidDataException("Mutation did not change candidate");
            byte[] mutated = ScalarCandidate.Compile(code, plan.Carrier);
            Write("omit-" + map.Target + ".cs", System.Text.Encoding.UTF8.GetBytes(code));
            Write("omit-" + map.Target + ".dll", mutated);
            var mutantContext = new AssemblyLoadContext("Omit_" + map.Target, isCollectible: true);
            using var mutantStream = new MemoryStream(mutated);
            var assembly = mutantContext.LoadFromStream(mutantStream);
            Type stateType = assembly.GetType("GeneratedCandidate.CandidateState", true)!;
            MethodInfo invoke = assembly.GetType("GeneratedCandidate.Candidate", true)!.GetMethod("Run")!;
            int differences = 0;
            for (int i = 0; i < states.Count; i++)
            {
                object box = Activator.CreateInstance(stateType)!;
                foreach (var pair in states[i]) stateType.GetField(pair.Key)!.SetValue(box, pair.Value);
                object?[] arguments = new object?[] { box }.Concat(parameters[i]).ToArray();
                invoke.Invoke(null, arguments);
                var actual = states[i].Keys.ToDictionary(k => k, k => stateType.GetField(k)!.GetValue(arguments[0])!);
                if (Canonical(actual) != Canonical(expected[i])) differences++;
            }
            if (differences == 0) throw new InvalidDataException("Cases fail to detect omitted field write: " + map.Target);
            mutants.Add(new { Mutation = "omit-field-write:" + map.Target, Cases = states.Count, Differences = differences,
                Assertion = "Complete two-state projection differs from untouched official reference", AssemblySha256 = Sha(mutated) });
            mutantContext.Unload();
        }
        candidateContext.Unload(); originalContext.Unload();
        if (Sha(File.ReadAllBytes(runtimeReference)) != runtimeReferenceSha256) throw new InvalidDataException("Runtime compilation reference changed");
        return new { SchemaVersion = 1, Kind = "source-scalar-candidate-verification", Accepted = true, CandidateOnly = true,
            Entry = plan.Inventory.Entries.Single(), Cases = observations.Count, OriginalMethodCalls = observations.Count,
            OriginalCarrierCompiled = true, WholeOriginalTypeCompiled = false,
            RuntimeReference = new { Path = runtimeReference, Sha256 = runtimeReferenceSha256 },
            CandidateSha256 = Sha(System.Text.Encoding.UTF8.GetBytes(plan.Candidate)),
            OriginalCarrierSha256 = Sha(System.Text.Encoding.UTF8.GetBytes(plan.Carrier)), AssemblySha256 = Sha(binary),
            plan.Context.SourceSha256, plan.Context.ReferenceSha256,
            CompilationErrorCount = plan.Inventory.CompilationErrorCount, MethodQualified = true,
            OriginalRngCursorAndAll56SlotsUnchanged = true, Mutants = mutants, Observations = observations,
            Scope = "Automatic bounded scalar field adaptation, compiled proper original-method carrier and candidate, actual untouched official method oracle. No full AI, transitive gameplay ownership, production admission or general equivalence proof." };

        void Write(string name, byte[] bytes)
        {
            using var file = new FileStream(Path.Combine(outputDirectory, name), FileMode.CreateNew, FileAccess.Write);
            file.Write(bytes);
        }
    }
}
