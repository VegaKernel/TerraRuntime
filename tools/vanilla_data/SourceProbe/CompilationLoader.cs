using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SourceProbe;

internal sealed record CompilationContext(
    CSharpCompilation Compilation,
    IReadOnlyDictionary<string, SyntaxTree> Trees,
    IReadOnlyDictionary<string, string> SourceSha256,
    IReadOnlyDictionary<string, string> ReferenceSha256);

internal static class CompilationLoader
{
    private const int MaximumSourceCount = 256;
    private const int MaximumSourceBytes = 32 * 1024 * 1024;
    private const long MaximumTotalSourceBytes = 128L * 1024 * 1024;
    private const int MaximumReferenceBytes = 64 * 1024 * 1024;
    private const int MaximumReferenceCount = 128;
    private const long MaximumTotalBytes = 256L * 1024 * 1024;

    // This loader binds explicitly selected original source units against the
    // original SDK metadata. It never executes an original assembly or invents
    // replacement declarations. A selected type can conflict with metadata
    // still returned by another unit; callers must retain the real diagnostics
    // and explicitly add the coherent source units needed by their entry.
    internal static CompilationContext Load(string sourceRoot, string assemblyPath,
        IReadOnlyList<string> relativeSources)
    {
        ArgumentNullException.ThrowIfNull(relativeSources);
        if (relativeSources.Count is < 1 or > MaximumSourceCount)
            throw new ArgumentException($"Expected 1..{MaximumSourceCount} source files.", nameof(relativeSources));
        string root = Path.GetFullPath(sourceRoot);
        string executable = Path.GetFullPath(assemblyPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        RejectLink(root);
        RejectLink(executable);
        string sdk = Path.GetDirectoryName(executable)!;
        var trees = new SortedDictionary<string, SyntaxTree>(StringComparer.Ordinal);
        var sourceHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var referenceHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var sourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        long totalSourceBytes = 0;
        foreach (string relative in relativeSources)
        {
            if (string.IsNullOrWhiteSpace(relative) || relative.Length > 1024 ||
                Path.IsPathRooted(relative) || relative.Contains(':') || relative.Contains('\\') ||
                relative.Split('/').Any(part => part is "" or "." or "..") ||
                !relative.EndsWith(".cs", StringComparison.Ordinal) || !sourceNames.Add(relative))
                throw new ArgumentException("Sources must be distinct relative forward-slash .cs paths without traversal.", nameof(relativeSources));
            string path = Path.GetFullPath(Path.Combine(root, relative));
            string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(rootPrefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new ArgumentException("Source path escapes the declared source root.", nameof(relativeSources));
            for (string? parent = path; parent is not null && parent != root; parent = Path.GetDirectoryName(parent))
                RejectLink(parent);
            byte[] bytes = ReadBounded(path, MaximumSourceBytes);
            totalSourceBytes += bytes.Length;
            if (totalSourceBytes > MaximumTotalSourceBytes) throw new InvalidDataException("Source input budget exceeded.");
            AddBudget(bytes.Length);
            string source = new UTF8Encoding(false, true).GetString(bytes);
            if (source.StartsWith('\uFEFF')) source = source[1..];
            trees.Add(relative, CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.CSharp14), path));
            sourceHashes.Add(relative, Hash(bytes));
        }

        var metadata = new List<MetadataReference>();
        var identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byte[] original = ReadBounded(executable, MaximumReferenceBytes);
        AddBudget(original.Length);
        AddManagedReference(Path.GetFileName(executable), original, required: true);
        string[] siblings = Directory.EnumerateFiles(sdk, "*.dll", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal).Take(MaximumReferenceCount + 1).ToArray();
        if (siblings.Length > MaximumReferenceCount) throw new InvalidDataException("Too many SDK reference files.");
        foreach (string path in siblings)
        {
            RejectLink(path);
            byte[] bytes = ReadBounded(path, MaximumReferenceBytes);
            AddBudget(bytes.Length);
            AddManagedReference(Path.GetFileName(path), bytes, required: false);
        }

        // Embedded managed DLLs (notably ReLogic) carry types in public method
        // signatures. Resolve their exact original bytes in memory rather than
        // loading Terraria, writing extracted binaries or silently stubbing them.
        using (var pe = new PEReader(ImmutableArray.CreateRange(original)))
        {
            var reader = pe.GetMetadataReader();
            var resources = pe.PEHeaders.CorHeader!.ResourcesDirectory;
            if (resources.Size > 0)
            {
                var content = pe.GetSectionData(resources.RelativeVirtualAddress).GetContent(0, resources.Size);
                int count = 0;
                foreach (var handle in reader.ManifestResources)
                {
                    var resource = reader.GetManifestResource(handle);
                    string name = reader.GetString(resource.Name);
                    if (!resource.Implementation.IsNil || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                    if (++count > MaximumReferenceCount) throw new InvalidDataException("Too many embedded SDK references.");
                    if (resource.Offset > int.MaxValue || (int)resource.Offset > content.Length - sizeof(int))
                        throw new InvalidDataException("Invalid embedded reference offset.");
                    int offset = (int)resource.Offset;
                    int length = BinaryPrimitives.ReadInt32LittleEndian(content.AsSpan(offset, sizeof(int)));
                    if (length < 0 || length > MaximumReferenceBytes || length > content.Length - offset - sizeof(int))
                        throw new InvalidDataException("Invalid embedded reference length.");
                    AddBudget(length);
                    AddManagedReference(Path.GetFileName(executable) + "::" + name,
                        content.AsSpan(offset + sizeof(int), length).ToArray(), required: false);
                }
            }
        }
        var compilation = CSharpCompilation.Create("TerrariaSourceObservation", trees.Values,
            metadata, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true, concurrentBuild: false, checkOverflow: false));
        return new(compilation, trees, sourceHashes, referenceHashes);

        void AddBudget(int count)
        {
            totalBytes += count;
            if (totalBytes > MaximumTotalBytes) throw new InvalidDataException("Source/reference input budget exceeded.");
        }

        void AddManagedReference(string key, byte[] bytes, bool required)
        {
            string hash = Hash(bytes);
            referenceHashes.Add(key, hash);
            try
            {
                using var pe = new PEReader(ImmutableArray.CreateRange(bytes));
                if (!pe.HasMetadata)
                {
                    if (required) throw new InvalidDataException("Official assembly has no managed metadata.");
                    return; // Native sidecars remain pinned input, never metadata references.
                }
                var reader = pe.GetMetadataReader();
                if (!reader.IsAssembly) throw new InvalidDataException("SDK reference is not an assembly.");
                var definition = reader.GetAssemblyDefinition();
                string identity = reader.GetString(definition.Name) + "," + definition.Version + "," +
                    reader.GetString(definition.Culture) + "," + Convert.ToHexString(reader.GetBlobBytes(definition.PublicKey));
                if (identities.TryGetValue(identity, out string? previous))
                {
                    if (previous != hash) throw new InvalidDataException("Conflicting SDK assembly identity: " + identity);
                    return;
                }
                identities.Add(identity, hash);
                metadata.Add(MetadataReference.CreateFromImage(ImmutableArray.CreateRange(bytes), filePath: key));
            }
            catch (BadImageFormatException) when (!required)
            {
                // A native DLL can lack a PE/CLI header. Its actual bytes stay
                // pinned, and no synthetic managed symbol replaces it.
            }
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] ReadBounded(string path, int maximum)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximum) throw new InvalidDataException("Input exceeds byte limit: " + path);
        using var buffer = new MemoryStream((int)stream.Length);
        byte[] chunk = new byte[64 * 1024];
        while (true)
        {
            int count = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, maximum + 1L - buffer.Length));
            if (count == 0) return buffer.ToArray();
            buffer.Write(chunk, 0, count);
            if (buffer.Length > maximum) throw new InvalidDataException("Input grew past byte limit: " + path);
        }
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked source/reference inputs are unsupported: " + path);
    }
}
