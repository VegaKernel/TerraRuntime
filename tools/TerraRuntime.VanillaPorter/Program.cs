using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerraRuntime.VanillaPorter;

internal static class Program
{
    private const string Help = """
        VanillaPorter (experimental, syntax-only).
        scan     --file <path.cs> --method <name> [--out <folder>]
        scan     --source <decompiled/1458> --npc-ai <number> [--out <folder>]
        scaffold --file <path.cs> --method <name> --mapping <map.json> [--out <folder>]
        verify   --file <synthetic.cs> --method <name> --mapping <map.json>
        Output defaults to artifacts/vanillaporter. Scaffold is fail-closed, not a vanilla parity proof.
        """;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "help" or "-h")
            {
                Console.WriteLine(Help);
                return 0;
            }
            var command = args[0];
            if (command is not ("scan" or "scaffold" or "verify"))
                throw new InvalidOperationException("Unknown command: " + command);
            var options = ParseOptions(args.Skip(1).ToArray());
            var (path, source) = LoadSource(options);
            var method = FindMethod(source, options);
            var report = Analyze(path, source, method);
            var output = Path.GetFullPath(options.GetValueOrDefault("out", "artifacts/vanillaporter"));
            GuardOutput(output);

            if (command == "verify")
            {
                var mapping = LoadMapping(options);
                var generated = Transform(method, mapping);
                VerifySynthetic(source, generated);
                Console.WriteLine("PASS: synthetic source/generated behavioral comparison. No Terraria parity claim.");
                return 0;
            }

            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "analysis.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n",
                new UTF8Encoding(false));

            if (command == "scaffold")
            {
                var generated = Transform(method, LoadMapping(options));
                File.WriteAllText(Path.Combine(output, "PortedCandidate.cs"), generated, new UTF8Encoding(false));
                Console.WriteLine("Generated candidate C# (not admitted into TerraRuntime).");
            }

            Console.WriteLine($"Analyzed {method.Identifier.ValueText}; report in {output}");
            return 0;
        }
        catch (Exception e) when (e is IOException or ArgumentException or JsonException
                                or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("VanillaPorter refused: " + e.Message);
            return 2;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) ||
                i + 1 == args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new InvalidOperationException("Expected --option value: " + args[i]);
            if (!options.TryAdd(args[i][2..], args[++i]))
                throw new InvalidOperationException("Duplicate option: " + args[i - 1]);
        }
        return options;
    }

    private static (string Path, string Text) LoadSource(Dictionary<string, string> options)
    {
        if (options.TryGetValue("file", out var file))
            return (Path.GetFullPath(file), File.ReadAllText(file));
        if (!options.TryGetValue("source", out var directory) || !Directory.Exists(directory))
            throw new InvalidOperationException("Provide --file or an existing --source directory.");

        var paths = Directory.EnumerateFiles(directory,
                options.ContainsKey("npc-ai") ? "NPC.cs" : "*.cs", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (paths.Length != 1)
            throw new InvalidOperationException($"Found {paths.Length} candidate files. Provide --file explicitly.");
        return (Path.GetFullPath(paths[0]), File.ReadAllText(paths[0]));
    }

    private static MethodDeclarationSyntax FindMethod(string text, Dictionary<string, string> options)
    {
        var tree = CSharpSyntaxTree.ParseText(text);
        var errors = tree.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).Take(3).ToArray();
        if (errors.Length != 0)
            throw new InvalidOperationException("Source syntax errors: " + string.Join("; ", errors.Select(x => x.ToString())));

        string name;
        if (options.TryGetValue("method", out var methodName))
            name = methodName;
        else if (options.TryGetValue("npc-ai", out var style) && int.TryParse(style, out var ai) && ai is >= 0 and <= 999)
            name = $"AI_{ai:000}";
        else
            throw new InvalidOperationException("Provide --method or a numeric --npc-ai.");
        var methods = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(x => x.Identifier.ValueText == name).Take(2).ToArray();
        if (methods.Length != 1)
            throw new InvalidOperationException($"Expected one method named {name}; found {methods.Length}. No method inferred.");
        return methods[0];
    }

    private static object Analyze(string path, string source, MethodDeclarationSyntax method)
    {
        var members = method.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(x => x.Parent is not MemberAccessExpressionSyntax p || p.Expression != x)
            .Select(x => x.ToString()).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var calls = method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(x => x.Expression.ToString()).ToArray();
        return new
        {
            version = "Terraria 1.4.5.8",
            sourceFile = Path.GetFileName(path),
            sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant(),
            method = method.Identifier.ValueText,
            analysisScope = "syntax-only; unresolved transitive dependencies; no semantic equivalence proof",
            memberReferences = members,
            directCalls = calls,
            possibleRngCalls = calls.Where(x => x.Contains("rand", StringComparison.OrdinalIgnoreCase)
                                               || x.Contains("random", StringComparison.OrdinalIgnoreCase)).ToArray(),
            conditions = method.DescendantNodes().OfType<IfStatementSyntax>().Select(x => x.Condition.ToString()).ToArray(),
            unsupportedScaffoldNodes = Unsupported(method),
        };
    }

    private static PortMapping LoadMapping(IReadOnlyDictionary<string, string> options)
    {
        if (!options.TryGetValue("mapping", out var path))
            throw new InvalidOperationException("Provide --mapping <file.json>.");
        var map = JsonSerializer.Deserialize<PortMapping>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (map is null || map.Members is null || map.Members.Count == 0)
            throw new InvalidOperationException("Mapping has no members.");
        if (!SyntaxFacts.IsValidIdentifier(map.SourceParameter)
            || !SyntaxFacts.IsValidIdentifier(map.TargetParameter)
            || !SyntaxFacts.IsValidIdentifier(map.TargetClass)
            || SyntaxFactory.ParseTypeName(map.TargetType).ContainsDiagnostics)
            throw new InvalidOperationException("Invalid target identifiers or type.");
        return map;
    }

    private static void GuardOutput(string output)
    {
        var parts = output.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Any(x => x is "src" or "tests" or "decompiled"))
            throw new InvalidOperationException("Output inside src, tests or decompiled is forbidden.");
    }

    private static string[] Unsupported(MethodDeclarationSyntax method)
    {
        if (method.Body is null)
            return ["No method body"];
        return method.Body.DescendantNodesAndSelf()
            .Where(x => x switch
            {
                BlockSyntax or IfStatementSyntax or ElseClauseSyntax or ExpressionStatementSyntax
                    or IdentifierNameSyntax or MemberAccessExpressionSyntax or LiteralExpressionSyntax
                    or ParenthesizedExpressionSyntax => false,
                BinaryExpressionSyntax b => b.Kind() is not (SyntaxKind.AddExpression or SyntaxKind.SubtractExpression
                    or SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression
                    or SyntaxKind.LessThanExpression or SyntaxKind.GreaterThanExpression
                    or SyntaxKind.LessThanOrEqualExpression or SyntaxKind.GreaterThanOrEqualExpression
                    or SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression
                    or SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression),
                AssignmentExpressionSyntax a => a.Kind() is not (SyntaxKind.SimpleAssignmentExpression
                    or SyntaxKind.AddAssignmentExpression or SyntaxKind.SubtractAssignmentExpression),
                PrefixUnaryExpressionSyntax p => p.Kind() is not (SyntaxKind.UnaryMinusExpression or SyntaxKind.UnaryPlusExpression
                    or SyntaxKind.LogicalNotExpression),
                _ => true
            })
            .Select(x => x.Kind().ToString()).Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string Transform(MethodDeclarationSyntax method, PortMapping map)
    {
        var unsupported = Unsupported(method);
        if (unsupported.Length != 0)
            throw new InvalidOperationException("Unsupported method syntax: " + string.Join(", ", unsupported));
        if (method.ReturnType is not PredefinedTypeSyntax type || !type.Keyword.IsKind(SyntaxKind.VoidKeyword)
            || !method.Modifiers.Any(SyntaxKind.StaticKeyword)
            || method.TypeParameterList is not null || method.AttributeLists.Count != 0)
            throw new InvalidOperationException("Only non-generic, attribute-free static void methods are supported.");
        var parameters = method.ParameterList.Parameters;
        var parameter = parameters.FirstOrDefault(x => x.Identifier.ValueText == map.SourceParameter);
        if (parameter is null || parameter != parameters[0]
            || parameters.Skip(1).Any(x => x.Identifier.ValueText == map.TargetParameter))
            throw new InvalidOperationException("Mapping source parameter must be the first, with a distinct target.");

        // Map only complete member expressions: partial maps must never silently change the meaning.
        var accesses = method.Body!.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(x => x.Parent is not MemberAccessExpressionSyntax p || p.Expression != x);
        foreach (var access in accesses)
        {
            if (!map.Members.ContainsKey(access.ToString()))
                throw new InvalidOperationException("Unmapped member access: " + access);
        }
        var permitted = new HashSet<string>(parameters.Select(x => x.Identifier.ValueText), StringComparer.Ordinal);
        foreach (var id in method.Body.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (id.Parent is MemberAccessExpressionSyntax m && m.Name == id)
                continue;
            if (!permitted.Contains(id.Identifier.ValueText))
                throw new InvalidOperationException("Unresolved identifier: " + id);
            if (id.Identifier.ValueText == map.SourceParameter && id.Parent is not MemberAccessExpressionSyntax)
                throw new InvalidOperationException("Direct source-parameter access is unsupported.");
        }
        foreach (var expression in map.Members.Values)
            if (SyntaxFactory.ParseExpression(expression).ContainsDiagnostics
                || expression.Contains(';') || expression.Contains('\n'))
                throw new InvalidOperationException("Invalid mapped expression: " + expression);

        var body = (BlockSyntax)new MemberRewriter(map.Members).Visit(method.Body)!;
        var changed = parameter.WithIdentifier(SyntaxFactory.Identifier(map.TargetParameter))
            .WithType(SyntaxFactory.ParseTypeName(map.TargetType));
        var result = method.WithBody(body)
            .WithParameterList(method.ParameterList.WithParameters(parameters.Replace(parameter, changed)))
            .WithLeadingTrivia(default(SyntaxTriviaList))
            .WithTrailingTrivia(default(SyntaxTriviaList));
        var code = "// Candidate only: not validated against official Terraria.\n"
            + "namespace GeneratedVanilla;\npublic static class " + map.TargetClass + "\n{\n"
            + result.NormalizeWhitespace().ToFullString() + "\n}\n";
        if (CSharpSyntaxTree.ParseText(code).GetDiagnostics().Any(x => x.Severity == DiagnosticSeverity.Error))
            throw new InvalidOperationException("Generated C# has syntax errors.");
        return code;
    }

    private sealed class MemberRewriter(Dictionary<string, string> members) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            return members.TryGetValue(node.ToString(), out var mapped)
                ? SyntaxFactory.ParseExpression(mapped).WithTriviaFrom(node)
                : base.VisitMemberAccessExpression(node);
        }
    }

    private static void VerifySynthetic(string source, string generated)
    {
        if (!source.Contains("class VanillaActor", StringComparison.Ordinal)
            || !source.Contains("class OriginalMotion", StringComparison.Ordinal))
            throw new InvalidOperationException("Differential verify is restricted to the synthetic example, not actual Terraria.");

        var a = Compile(source, "VanillaPorterOriginal");
        var b = Compile(generated + "\npublic sealed class PortedActor { public float Position; public float Speed; }\n",
            "VanillaPorterAdapted");

        var actorA = a.GetType("VanillaActor")!;
        var actorB = b.GetType("GeneratedVanilla.PortedActor")!;
        var runA = a.GetType("OriginalMotion")!.GetMethod("Step")!;
        var runB = b.GetType("GeneratedVanilla.PortedMotion")!.GetMethod("Step")!;
        foreach (float x in new[] { -10f, -1f, 0f, 1f, 10f })
        foreach (float target in new[] { -11f, 0f, 11f })
        {
            var va = Activator.CreateInstance(actorA)!;
            var vb = Activator.CreateInstance(actorB)!;
            actorA.GetField("X")!.SetValue(va, x);
            actorB.GetField("Position")!.SetValue(vb, x);
            runA.Invoke(null, [va, target]);
            runB.Invoke(null, [vb, target]);
            var expected = (float)actorA.GetField("Velocity")!.GetValue(va)!;
            var actual = (float)actorB.GetField("Speed")!.GetValue(vb)!;
            if (expected != actual)
                throw new InvalidOperationException($"Differential mismatch: X={x}, target={target}: {expected} != {actual}");
        }
        Console.WriteLine("15/15 synthetic differential cases passed.");
    }

    private static Assembly Compile(string code, string name)
    {
        var trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Runtime references unavailable.");
        var references = trusted.Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(name,
            [CSharpSyntaxTree.ParseText(code)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
            throw new InvalidOperationException("Compilation failed: "
                + string.Join("; ", result.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error)));
        return Assembly.Load(stream.ToArray());
    }
}

internal sealed class PortMapping
{
    public string SourceParameter { get; set; } = "";
    public string TargetParameter { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string TargetClass { get; set; } = "";
    public Dictionary<string, string> Members { get; set; } = new(StringComparer.Ordinal);
}
