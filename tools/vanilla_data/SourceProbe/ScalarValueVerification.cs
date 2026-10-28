using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SourceProbe;

internal static class ScalarValueVerification
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Canonical(object? value) => JsonSerializer.Serialize(value);
    private static Type RuntimeType(string name) => name switch { "int" => typeof(int), "bool" => typeof(bool), "float" => typeof(float), _ => throw new InvalidDataException("Unknown primitive") };

    internal static object Run(ValuePlan plan, string outputDirectory)
    {
        string original = Path.GetFullPath(plan.Request.Analysis.Assembly);
        if (Sha(File.ReadAllBytes(original)) != "4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034" ||
            Sha(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(original)!, "FNA.dll"))) != "46d201f59ce6c3827414b2c2c5141c218acabda605b02e564d73ca2efb2a3171")
            throw new InvalidDataException("Value execution requires admitted original1458/FNA");
        string runtimeReference = typeof(object).Assembly.Location, runtimeSha = Sha(File.ReadAllBytes(runtimeReference));
        var originalContext = new AssemblyLoadContext("ValueOriginal", isCollectible: true);
        Assembly? game = null;
        originalContext.Resolving += (_, name) =>
        {
            string file = Path.Combine(Path.GetDirectoryName(original)!, name.Name + ".dll");
            if (File.Exists(file)) return originalContext.LoadFromAssemblyPath(file);
            string? resource = game?.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith("." + name.Name + ".dll", StringComparison.OrdinalIgnoreCase));
            if (resource is null) return null;
            using var stream = game!.GetManifestResourceStream(resource)!;
            return originalContext.LoadFromStream(stream);
        };
        game = originalContext.LoadFromAssemblyPath(original);
        game.GetType("Terraria.Program", true)!.GetField("SavePath", Flags)!.SetValue(null, outputDirectory);
        Type main = game.GetType("Terraria.Main", true)!;
        main.GetField("dedServ", Flags)!.SetValue(null, true); main.GetField("netMode", Flags)!.SetValue(null, 2);
        Type actorType = game.GetType(plan.Method.ContainingType.ToDisplayString(), true)!;
        var parameters = plan.Method.Parameters;
        int receiverIndex = Enumerable.Range(0, parameters.Length).Single(i => !ScalarValueCandidate.Scalar(parameters[i].Type));
        Type receiverType = game.GetType(parameters[receiverIndex].Type.ToDisplayString(), true)!;
        Type[] argumentTypes = parameters.Select(p => ScalarValueCandidate.Scalar(p.Type) ? RuntimeType(ScalarValueCandidate.Type(p.Type)) : receiverType).ToArray();
        MethodInfo method = actorType.GetMethod(plan.Method.Name, Flags, argumentTypes) ?? throw new InvalidDataException("Exact original value method absent");
        if (method.IsStatic || method.IsVirtual || method.DeclaringType != actorType || method.ReturnType != RuntimeType(plan.Request.ReturnType) || method.GetParameters().Any(p => p.ParameterType.IsByRef))
            throw new InvalidDataException("Original runtime value signature differs");
        var fields = plan.Fields.ToDictionary(pair => pair.Value.Target, pair =>
        {
            Type owner = pair.Value.Receiver == "this" ? actorType : receiverType;
            FieldInfo field = owner.GetField(pair.Key.Name, Flags) ?? throw new InvalidDataException("Original mapped field absent");
            if (field.DeclaringType != owner || field.IsStatic || field.FieldType != RuntimeType(pair.Value.Type)) throw new InvalidDataException("Original field shape differs");
            return field;
        });
        Type randomType = game.GetType("Terraria.Utilities.UnifiedRandom", true)!;
        FieldInfo randomOwner = main.GetField("rand", Flags)!, cursor = randomType.GetField("inext", Flags)!, slots = randomType.GetField("SeedArray", Flags)!;
        object Rng(object random)
        {
            int[] values = ((int[])slots.GetValue(random)!).ToArray();
            if (values.Length != 56) throw new InvalidDataException("Physical RNG shape differs");
            return new { Cursor = Convert.ToUInt32(cursor.GetValue(random)), Slots = values };
        }
        byte[] binary = ScalarCandidate.Compile(plan.Candidate, plan.Carrier);
        Write("candidate.cs", Encoding.UTF8.GetBytes(plan.Candidate)); Write("original-carrier.cs", Encoding.UTF8.GetBytes(plan.Carrier)); Write("candidate.dll", binary);
        var generatedContext = new AssemblyLoadContext("ValueCandidate", isCollectible: true);
        using var binaryInput = new MemoryStream(binary);
        Assembly assembly = generatedContext.LoadFromStream(binaryInput);
        Type carrier = assembly.GetType("OriginalMethodCarrier.Carrier", true)!, other = assembly.GetType("OriginalMethodCarrier.CarrierReceiver", true)!, state = assembly.GetType("GeneratedCandidate.CandidateState", true)!;
        MethodInfo carrierMethod = carrier.GetMethod(plan.Method.Name)!, candidateMethod = assembly.GetType("GeneratedCandidate.Candidate", true)!.GetMethod("Run")!;
        var rows = new List<object>();
        var expected = new List<object>();
        var states = new List<Dictionary<string, object>>();
        var scalarArguments = new List<object?[]>();
        int index = 0;
        foreach (var item in plan.Request.Cases)
        {
            var initial = plan.Fields.Values.ToDictionary(f => f.Target, f => ScalarValueCandidate.Value(item.State[f.Target], f.Type));
            object actor = Activator.CreateInstance(actorType)!, receiver = Activator.CreateInstance(receiverType)!;
            foreach (var map in plan.Fields.Values) fields[map.Target].SetValue(map.Receiver == "this" ? actor : receiver, initial[map.Target]);
            object?[] args = parameters.Select((p, i) => ScalarValueCandidate.Scalar(p.Type) ? ScalarValueCandidate.Value(item.Arguments[i], ScalarValueCandidate.Type(p.Type)) : receiver).ToArray();
            var referenceIds = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            object actorBefore = Fingerprint(actor, referenceIds), receiverBefore = Fingerprint(receiver, referenceIds);
            object random = Activator.CreateInstance(randomType, [item.RngSeed])!; randomOwner.SetValue(null, random);
            object rngBefore = Rng(random);
            object result = ScalarValueCandidate.Observe(method.Invoke(actor, args) ?? throw new InvalidDataException("Missing original result"));
            object rngAfter = Rng(random);
            object actorAfter = Fingerprint(actor, referenceIds), receiverAfter = Fingerprint(receiver, referenceIds);
            if (Canonical(actorBefore) != Canonical(actorAfter) || Canonical(receiverBefore) != Canonical(receiverAfter) || Canonical(rngBefore) != Canonical(rngAfter))
                throw new InvalidDataException("Original value method changed an actor or physical RNG at case " + index);
            var observed = plan.Fields.Values.ToDictionary(f => f.Target, f => ScalarValueCandidate.Observe(fields[f.Target].GetValue(f.Receiver == "this" ? actor : receiver)!));
            var input = initial.ToDictionary(p => p.Key, p => ScalarValueCandidate.Observe(p.Value));
            if (Canonical(observed) != Canonical(input)) throw new InvalidDataException("Read-only mapped original input changed");
            object carrierActor = Activator.CreateInstance(carrier)!, carrierReceiver = Activator.CreateInstance(other)!;
            foreach (var map in plan.Fields)
                (map.Value.Receiver == "this" ? carrier : other).GetField(map.Key.Name)!.SetValue(map.Value.Receiver == "this" ? carrierActor : carrierReceiver, initial[map.Value.Target]);
            object?[] carrierArgs = args.Select((a, i) => i == receiverIndex ? carrierReceiver : a).ToArray();
            object carrierResult = ScalarValueCandidate.Observe(carrierMethod.Invoke(carrierActor, carrierArgs)!);
            var carrierAfter = plan.Fields.ToDictionary(p => p.Value.Target, p => ScalarValueCandidate.Observe((p.Value.Receiver == "this" ? carrier : other).GetField(p.Key.Name)!.GetValue(p.Value.Receiver == "this" ? carrierActor : carrierReceiver)!));
            object?[] scalarArgs = args.Where((_, i) => i != receiverIndex).ToArray();
            var candidate = Execute(assembly, candidateMethod, state, initial, scalarArgs);
            if (Canonical(result) != Canonical(carrierResult) || Canonical(result) != Canonical(candidate.Result) || Canonical(input) != Canonical(carrierAfter) || Canonical(input) != Canonical(candidate.State))
                throw new InvalidDataException("Original/carrier/generated value result or readonly state differs at case " + index);
            rows.Add(new { Index = index++, item.RngSeed, Initial = input, Arguments = item.Arguments, Original = result, Carrier = carrierResult, Candidate = candidate.Result,
                OriginalState = observed, CarrierState = carrierAfter, CandidateState = candidate.State,
                ActorBefore = actorBefore, ActorAfter = actorAfter, ReceiverBefore = receiverBefore, ReceiverAfter = receiverAfter, RngBefore = rngBefore, RngAfter = rngAfter });
            expected.Add(result); states.Add(initial); scalarArguments.Add(scalarArgs);
        }
        var controls = new List<object>();
        foreach (var mutant in ScalarValueCandidate.Mutants(plan))
        {
            byte[] bytes = ScalarCandidate.Compile(mutant.Value, plan.Carrier);
            Write(mutant.Key + ".cs", Encoding.UTF8.GetBytes(mutant.Value)); Write(mutant.Key + ".dll", bytes);
            var context = new AssemblyLoadContext(mutant.Key, isCollectible: true);
            using var input = new MemoryStream(bytes); Assembly mutated = context.LoadFromStream(input);
            Type mutatedState = mutated.GetType("GeneratedCandidate.CandidateState", true)!;
            MethodInfo invoke = mutated.GetType("GeneratedCandidate.Candidate", true)!.GetMethod("Run")!;
            int differences = 0;
            for (int i = 0; i < states.Count; i++)
            {
                var result = Execute(mutated, invoke, mutatedState, states[i], scalarArguments[i]);
                if (Canonical(expected[i]) != Canonical(result.Result)) differences++;
            }
            if (differences == 0) throw new InvalidDataException("Corpus does not detect value mutation: " + mutant.Key);
            controls.Add(new { Mutation = mutant.Key, Cases = states.Count, Differences = differences, Errors = 0, AssemblySha256 = Sha(bytes) });
            context.Unload();
        }
        generatedContext.Unload(); originalContext.Unload();
        if (Sha(File.ReadAllBytes(runtimeReference)) != runtimeSha) throw new InvalidDataException("Runtime reference drift");
        return new { SchemaVersion = 2, Kind = "source-value-candidate-verification", Accepted = true, CandidateOnly = true,
            Entry = plan.Inventory.Entries.Single(), ReturnType = plan.Request.ReturnType, Cases = rows.Count, OriginalMethodCalls = rows.Count,
            OriginalCarrierCompiled = true, WholeOriginalTypeCompiled = false, MethodQualified = true,
            OriginalRngCursorAndAll56SlotsUnchanged = true, AllDirectActorFieldsAndArraysUnchanged = true,
            FloatDomain = "Finite IEEE binary32 input/output only, raw uint32 bits including signed zero and subnormals; no cast, widening, NaN or infinity admission.",
            ActorFingerprintScope = "Every direct instance field, inherited fields, recursively represented value types and array elements; other object fields use stable reference identity, not transitive object internals. No permitted operation can write any reference or actor field.",
            RuntimeReference = new { Path = runtimeReference, Sha256 = runtimeSha },
            CandidateSha256 = Sha(Encoding.UTF8.GetBytes(plan.Candidate)), OriginalCarrierSha256 = Sha(Encoding.UTF8.GetBytes(plan.Carrier)), AssemblySha256 = Sha(binary),
            plan.Context.SourceSha256, plan.Context.ReferenceSha256, CompilationErrorCount = plan.Inventory.CompilationErrorCount,
            Mutants = controls, Observations = rows,
            Scope = "Offline automatically generated bounded scalar-return candidate, proper method-body carrier and untouched official method differential. Receiver construction/read-only projection is a test preimage, not production Item/equipment/player admission." };

        void Write(string name, byte[] bytes)
        {
            using var output = new FileStream(Path.Combine(outputDirectory, name), FileMode.CreateNew, FileAccess.Write); output.Write(bytes);
        }
    }

    private static (object Result, Dictionary<string, object> State) Execute(Assembly assembly, MethodInfo method, Type state, Dictionary<string, object> initial, object?[] scalarArgs)
    {
        object box = Activator.CreateInstance(state)!;
        foreach (var pair in initial) state.GetField(pair.Key)!.SetValue(box, pair.Value);
        object?[] args = new object?[] { box }.Concat(scalarArgs).ToArray();
        object result = ScalarValueCandidate.Observe(method.Invoke(null, args)!);
        return (result, initial.Keys.ToDictionary(k => k, k => ScalarValueCandidate.Observe(state.GetField(k)!.GetValue(args[0])!)));
    }

    private static object Fingerprint(object actor, Dictionary<object, int> identities)
    {
        int count = 0;
        int Identity(object value)
        {
            if (!identities.TryGetValue(value, out int identity)) { identity = identities.Count + 1; identities.Add(value, identity); }
            return identity;
        }
        object? Value(object? value, int depth)
        {
            if (++count > 65536 || depth > 16) throw new InvalidDataException("Actor fingerprint bound exceeded");
            if (value is null || value is string || value is bool || value is char || value is byte || value is sbyte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong) return value;
            if (value is float number) return new { FloatBits = BitConverter.SingleToUInt32Bits(number) };
            if (value is double wide) return new { DoubleBits = BitConverter.DoubleToUInt64Bits(wide) };
            Type type = value.GetType();
            if (type.IsEnum) return new { Enum = type.FullName, Value = value.ToString() };
            if (value is Array array)
            {
                var entries = new List<object?>(); foreach (object? element in array) entries.Add(Value(element, depth + 1));
                return new { Array = type.FullName, Identity = Identity(array), Shape = Enumerable.Range(0, array.Rank).Select(array.GetLength).ToArray(), Values = entries };
            }
            if (type.IsValueType) return Fields(value, depth + 1);
            return new { ReferenceType = type.FullName, Identity = Identity(value) };
        }
        object Fields(object value, int depth)
        {
            var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            for (Type? type = value.GetType(); type is not null; type = type.BaseType)
                foreach (var field in type.GetFields(Flags | BindingFlags.DeclaredOnly).Where(f => !f.IsStatic).OrderBy(f => f.Name, StringComparer.Ordinal))
                    result.Add(type.FullName + "." + field.Name, Value(field.GetValue(value), depth));
            return result;
        }
        object all = Fields(actor, 0);
        return new { Sha256 = Sha(Encoding.UTF8.GetBytes(Canonical(all))), Fields = ((SortedDictionary<string, object?>)all).Count };
    }
}
