using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SourceProbe;

internal sealed record DiagnosticFact(string Id, int Line, string Severity);
internal sealed record CallFact(int Offset, int Line, string? Symbol, string Dispatch,
    string Origin, bool Rng, bool RefArguments);
internal sealed record AccessFact(int Offset, int Line, string? Symbol, string Target, string Mode, bool IsStatic);
internal sealed record MethodFact(string Id, string Path, int Line, int Depth, string Type, string Name,
    string[] ParameterTypes, bool Qualified,
    List<DiagnosticFact> Diagnostics, List<CallFact> Calls, List<AccessFact> Accesses, List<string> Uncertainties);
internal sealed record InventoryResult(int SchemaVersion, string Kind, bool Accepted, bool AnalysisOnly,
    bool SourceClosureComplete, List<string> Entries, List<MethodFact> Methods, List<string> Errors,
    List<string> Frontiers, int CompilationErrorCount, IReadOnlyDictionary<string, string> SourceSha256,
    IReadOnlyDictionary<string, string> ReferenceSha256, string Compiler, string[] Limits);

internal static class SemanticInventory
{
    private static string Identity(IMethodSymbol symbol) => symbol.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    private static string TypeName(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    private static int Line(IOperation operation) => operation.Syntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    internal static InventoryResult Analyze(CompilationContext context, AnalysisRequest request)
    {
        var compilation = context.Compilation;
        var errors = new List<string>();
        var frontiers = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<string>();
        var methods = new List<MethodFact>();
        var pending = new Queue<(IMethodSymbol Symbol, MethodDeclarationSyntax Syntax, string Path, int Depth)>();
        var admitted = new HashSet<string>(StringComparer.Ordinal);
        var treePaths = context.Trees.ToDictionary(pair => pair.Value, pair => pair.Key);

        bool Enqueue(IMethodSymbol symbol, MethodDeclarationSyntax syntax, string path, int depth)
        {
            string id = Identity(symbol);
            if (admitted.Contains(id)) return true;
            if (depth > request.MaxDepth) { errors.Add("depth-limit: " + id); return false; }
            if (admitted.Count >= request.MaxMethods) { errors.Add("method-limit: " + id); return false; }
            admitted.Add(id); pending.Enqueue((symbol, syntax, path, depth)); return true;
        }

        foreach (var entry in request.Entries)
        {
            if (!context.Trees.TryGetValue(entry.Path, out var tree)) { errors.Add("entry-source-missing: " + entry.Path); continue; }
            var model = compilation.GetSemanticModel(tree);
            var matches = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Select(node => (Node: node, Symbol: model.GetDeclaredSymbol(node)))
                .Where(pair => pair.Symbol is { } symbol && symbol.ContainingType.ToDisplayString() == entry.Type &&
                    symbol.Name == entry.Method && symbol.Parameters.Select(p => TypeName(p.Type)).SequenceEqual(entry.ParameterTypes))
                .ToArray();
            if (matches.Length != 1) { errors.Add("entry-not-unique: " + entry.Type + "." + entry.Method); continue; }
            entries.Add(Identity(matches[0].Symbol!));
            Enqueue(matches[0].Symbol!, matches[0].Node, entry.Path, 0);
        }

        int operations = 0;
        while (pending.TryDequeue(out var next))
        {
            var model = compilation.GetSemanticModel(next.Syntax.SyntaxTree);
            var diagnostics = model.GetDiagnostics(next.Syntax.Span)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => new DiagnosticFact(d.Id, d.Location.GetLineSpan().StartLinePosition.Line + 1, "error")).ToList();
            var calls = new List<CallFact>(); var accesses = new List<AccessFact>();
            var uncertainties = new HashSet<string>(StringComparer.Ordinal);
            bool qualified = diagnostics.Count == 0;
            // The method-body operation retains implicit returns (including
            // expression-bodied ref returns) that a bare expression can omit.
            IOperation? body = model.GetOperation(next.Syntax);
            if (body is null) { qualified = false; errors.Add("method-body-missing: " + Identity(next.Symbol)); }
            var stack = new Stack<IOperation>(); if (body is not null) stack.Push(body);
            while (stack.TryPop(out var operation))
            {
                if (++operations > 2_000_000) { errors.Add("operation-limit"); qualified = false; pending.Clear(); break; }
                if (operation.Kind == OperationKind.Invalid) { qualified = false; uncertainties.Add("invalid-operation"); }
                if (operation.Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol || operation is IAddressOfOperation)
                    uncertainties.Add("unsafe-pointer-alias-effects-not-proven");
                if (operation is IUsingOperation or IUsingDeclarationOperation or IForEachLoopOperation or IAwaitOperation or ILockOperation)
                    uncertainties.Add("implicit-protocol-calls-not-expanded");
                if (operation is IVariableDeclaratorOperation { Symbol.RefKind: not RefKind.None } or
                    ILocalReferenceOperation { Local.RefKind: not RefKind.None } or
                    ISimpleAssignmentOperation { IsRef: true })
                    uncertainties.Add("ref-local-or-ref-assignment-alias-effects-not-proven");
                if (operation is IFunctionPointerInvocationOperation or IDynamicObjectCreationOperation)
                { qualified = false; uncertainties.Add("unresolved-indirect-call"); }
                if (operation is IDynamicInvocationOperation)
                { qualified = false; calls.Add(new(operation.Syntax.SpanStart, Line(operation), null, "dynamic", "unresolved", false, false)); }

                IMethodSymbol? call = operation switch
                {
                    IInvocationOperation invocation => invocation.TargetMethod,
                    IObjectCreationOperation creation => creation.Constructor,
                    IBinaryOperation binary => binary.OperatorMethod,
                    IUnaryOperation unary => unary.OperatorMethod,
                    IConversionOperation conversion => conversion.OperatorMethod,
                    ICompoundAssignmentOperation compound => compound.OperatorMethod,
                    IIncrementOrDecrementOperation increment => increment.OperatorMethod,
                    _ => null
                };
                if (call is not null)
                {
                    bool indirect = call.MethodKind == MethodKind.DelegateInvoke ||
                        operation is IInvocationOperation { IsVirtual: true };
                    string dispatch = call.MethodKind == MethodKind.DelegateInvoke ? "delegate" : indirect ? "virtual" : "direct";
                    var declaration = call.OriginalDefinition.DeclaringSyntaxReferences
                        .Select(reference => reference.GetSyntax()).OfType<MethodDeclarationSyntax>().SingleOrDefault();
                    string origin = declaration is not null ? "source" : "metadata-or-nonmethod-body";
                    bool rng = call.ContainingType.ToDisplayString() is "Terraria.Utilities.UnifiedRandom" or "System.Random";
                    bool refArguments = call.Parameters.Any(parameter => parameter.RefKind != RefKind.None);
                    calls.Add(new(operation.Syntax.SpanStart, Line(operation), Identity(call), dispatch, origin, rng, refArguments));
                    if (refArguments) uncertainties.Add("ref-out-alias-effects-not-proven");
                    if (call.ReturnsByRef || call.ReturnsByRefReadonly) uncertainties.Add("ref-return-alias-effects-not-proven");
                    if (indirect) frontiers.Add(dispatch + ": " + Identity(call));
                    else if (declaration is not null && treePaths.TryGetValue(declaration.SyntaxTree, out var path))
                        Enqueue(call, declaration, path, next.Depth + 1);
                    else frontiers.Add(origin + ": " + Identity(call));
                }

                if (operation is IFieldReferenceOperation field)
                    accesses.Add(new(operation.Syntax.SpanStart, Line(operation), field.Field.ToDisplayString(), "field", Mode(operation), field.Field.IsStatic));
                if (operation is IPropertyReferenceOperation property)
                {
                    accesses.Add(new(operation.Syntax.SpanStart, Line(operation), property.Property.ToDisplayString(), "property", Mode(operation), property.Property.IsStatic));
                    uncertainties.Add("property-accessor-effects-not-traversed");
                }
                if (operation is IArrayElementReferenceOperation array)
                {
                    string? root = array.ArrayReference is IFieldReferenceOperation f ? f.Field.ToDisplayString() : null;
                    accesses.Add(new(operation.Syntax.SpanStart, Line(operation), root, "array-element", Mode(operation), false));
                    uncertainties.Add("array-alias-and-index-effects-not-proven");
                }
                if (operation is IEventReferenceOperation) uncertainties.Add("event-accessor-effects-not-traversed");
                if (operation is IAnonymousFunctionOperation or ILocalFunctionOperation)
                { uncertainties.Add("nested-function-body-not-traversed-unless-selected"); continue; }
                foreach (var child in operation.ChildOperations.Reverse()) stack.Push(child);
            }
            if (next.Syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(node =>
                model.GetSymbolInfo(node).Symbol is null))
            { qualified = false; uncertainties.Add("unresolved-invocation"); }
            if (!qualified) errors.Add("method-not-qualified: " + Identity(next.Symbol));
            foreach (string uncertainty in uncertainties) frontiers.Add(uncertainty + ": " + Identity(next.Symbol));
            methods.Add(new(Identity(next.Symbol), next.Path,
                next.Syntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1, next.Depth,
                next.Symbol.ContainingType.ToDisplayString(), next.Symbol.Name,
                next.Symbol.Parameters.Select(parameter => TypeName(parameter.Type)).ToArray(), qualified,
                diagnostics, calls.OrderBy(call => call.Offset).ToList(), accesses.OrderBy(access => access.Offset).ToList(), uncertainties.Order().ToList()));
        }

        bool accepted = errors.Count == 0 && entries.Count == request.Entries.Length && methods.All(method => method.Qualified);
        return new(1, "source-semantic-inventory", accepted, true, accepted && frontiers.Count == 0, entries, methods,
            errors.Distinct().Order().ToList(), frontiers.Order().ToList(),
            compilation.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error), context.SourceSha256,
            context.ReferenceSha256, typeof(CSharpCompilation).Assembly.FullName!,
            ["Acceptance qualifies only the explicitly visited method spans; global compilation errors are reported separately.",
             "Offsets are lexical source positions, not dynamic execution order or proof of RNG scheduling.",
             "Resolved metadata/virtual/delegate calls and untraversed accessors remain opaque frontiers, never inferred pure.",
             "Alias, static initialization, exception and runtime owner-binding semantics are not proven.",
             "No translated source, algorithm equivalence, complete project compilation or vanilla parity is established."]);
    }

    private static string Mode(IOperation operation)
    {
        var target = operation;
        while (target.Parent is ITupleOperation or IParenthesizedOperation) target = target.Parent;
        return target.Parent switch
        {
            ISimpleAssignmentOperation assignment when ReferenceEquals(assignment.Target, target) => "write",
            IDeconstructionAssignmentOperation assignment when ReferenceEquals(assignment.Target, target) => "write",
            ICompoundAssignmentOperation assignment when ReferenceEquals(assignment.Target, target) => "read-write",
            IIncrementOrDecrementOperation => "read-write",
            IArgumentOperation { Parameter.RefKind: RefKind.Out } => "write",
            IArgumentOperation { Parameter.RefKind: RefKind.Ref } => "read-write",
            _ => "read"
        };
    }
}
