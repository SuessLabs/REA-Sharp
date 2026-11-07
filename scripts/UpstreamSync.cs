using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rea.Validation;

/// <summary>Inventories upstream changes without merging/importing its JavaScript implementation.</summary>
public static class UpstreamSync
{
    public sealed record Commit(string Sha, string AuthorDate, string Subject, string Status);
    public sealed record Change(string Path, string ChangeType, string Area, string Status, string? CSharpOwner);
    public static async Task Generate(string root, string start, string target, string destination)
    {
        if (!IsRevision(start) || !IsRevision(target)) throw new ArgumentException("Use hexadecimal commit IDs (7-40 characters)");
        root = Path.GetFullPath(root); destination = Path.GetFullPath(destination);
        if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Choose a new inventory path; existing evidence is preserved");
        var first = (await Git(root, "rev-parse", "--verify", start + "^{commit}")).Trim();
        var last = (await Git(root, "rev-parse", "--verify", target + "^{commit}")).Trim();
        await Git(root, "merge-base", "--is-ancestor", first, last);
        var parent = (await Git(root, "rev-parse", first + "^")).Trim();
        var range = parent + ".." + last;
        var log = await Git(root, "log", "--reverse", "--topo-order", "--format=%H%x00%aI%x00%s%x00", range);
        var commits = ParseCommits(log);
        var changed = ParseChanges(await Git(root, "diff", "--no-renames", "--name-status", "-z", parent, last));
        var foreign = Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.Split('/').Any(part => part is ".git" or "bin" or "obj" or "node_modules" or "artifacts" or "_reference" or ".cache"))
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".js" or ".jsx" or ".ts" or ".tsx" or ".mjs" or ".cjs" or ".mts" or ".cts")
            .Order(StringComparer.Ordinal).ToArray();
        var owners = new[] { "dotnet/src/Rea.Domain/ElfHeader.cs", "dotnet/src/Rea.Domain/MachOHeader.cs", "dotnet/src/Rea.Domain/JvmClassFile.cs", "dotnet/src/Rea.Domain/DosMz.cs", "dotnet/src/Rea.Managed/ManagedStreamHeaders.cs", "dotnet/src/Rea.Managed/ManagedArtifactInspector.cs", "dotnet/src/Rea.Cli/McpServer.cs", "dotnet/src/Rea.Cli/SetupCommand.cs", "scripts/WebsiteBuilder.cs" };
        var implementations = owners.Select(path => new { path, sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, path)))) }).ToArray();
        var report = new
        {
            schema_version = 1,
            upstream_repository = "https://github.com/morluto/rea",
            start_inclusive = first, baseline_parent = parent, target_inclusive = last,
            fork_head = (await Git(root, "rev-parse", "HEAD")).Trim(),
            scope = "Complete commit and net-path inventory; selective C# semantic ports. Pending entries are not synchronized capabilities.",
            sync_complete = false,
            commits, changed_paths = changed,
            partial_ports = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(root, "scripts/upstream-ports.json"))),
            csharp_implementation_hashes = implementations,
            existing_javascript_typescript_paths = foreign,
            limitations = new[] { "Inventory is not proof that changes were ported.", "Merge commits and their reachable histories are inventoried; net file changes compare the pinned baseline parent and target.", "No JavaScript, Node/npm source or executable upstream hooks are imported or run.", "The legacy website is statically adapted with a C# publisher; JavaScript-only browser/Electron demos are retired, not ported." }
        };
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(output, report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    }
    public static bool IsRevision(string value) => Regex.IsMatch(value, @"\A[0-9a-fA-F]{7,40}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    public static Commit[] ParseCommits(string log)
    {
        var tokens = log.Split('\0');
        if (tokens.Length < 4 || tokens.Length % 3 != 1 || tokens[^1].Trim().Length != 0) throw new InvalidDataException("Malformed Git commit inventory");
        var commits = new List<Commit>();
        for (var index = 0; index < tokens.Length - 1; index += 3)
        {
            var sha = tokens[index].Trim('\r', '\n');
            if (sha.Length != 40 || !IsRevision(sha) || !DateTimeOffset.TryParse(tokens[index + 1], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                throw new InvalidDataException("Invalid commit identity/date");
            commits.Add(new(sha, tokens[index + 1], tokens[index + 2], "pending-semantic-review"));
        }
        if (commits.Count != commits.Select(commit => commit.Sha).Distinct(StringComparer.Ordinal).Count()) throw new InvalidDataException("Duplicate commits");
        return commits.ToArray();
    }
    public static Change[] ParseChanges(string output)
    {
        var parts = output.Split('\0');
        if (parts.Length % 2 != 1 || parts[^1].Length != 0) throw new InvalidDataException("Malformed Git changed-path inventory");
        var result = new List<Change>();
        for (var index = 0; index < parts.Length - 1; index += 2)
        {
            var kind = parts[index]; var path = parts[index + 1];
            if (kind is not ("A" or "M" or "D" or "T") || path.Length == 0 || path.StartsWith('/') || path.Split('/').Contains("..", StringComparer.Ordinal))
                throw new InvalidDataException("Invalid changed-path record");
            var owner = Owner(path);
            result.Add(new(path, kind, Area(path), owner is null ? "pending-capability-or-support-review" : "pending-csharp-owner-review", owner));
        }
        if (result.Count != result.Select(item => item.Path).Distinct(StringComparer.Ordinal).Count()) throw new InvalidDataException("Duplicate changed paths");
        return result.ToArray();
    }
    private static string Area(string path) => path.StartsWith("src/dotnet/", StringComparison.Ordinal) ? "managed-analysis"
        : path.StartsWith("src/domain/", StringComparison.Ordinal) ? "domain"
        : path.StartsWith("src/", StringComparison.Ordinal) ? "application-provider-or-adapter"
        : path.StartsWith("scripts/", StringComparison.Ordinal) || path.StartsWith(".github/", StringComparison.Ordinal) ? "automation"
        : path.StartsWith("website/", StringComparison.Ordinal) ? "website"
        : path.StartsWith("bridge/", StringComparison.Ordinal) ? "engine-bridge" : "support-fixtures-or-documentation";
    private static string? Owner(string path) => path switch
    {
        "src/domain/binaryTarget.ts" => "dotnet/src/Rea.Domain/ExecutableHeader.cs",
        "src/domain/elfFixedHeader.ts" => "dotnet/src/Rea.Domain/ElfHeader.cs",
        "src/domain/dosMz.ts" => "dotnet/src/Rea.Domain/DosMz.cs",
        "src/domain/apple/jvmClassFile.ts" => "dotnet/src/Rea.Domain/JvmClassFile.cs",
        "src/domain/apple/machoContainer.ts" or "src/domain/apple/machoLoadCommands.ts" => "dotnet/src/Rea.Domain/MachOHeader.cs",
        _ when path.StartsWith("src/dotnet/ManagedMetadata", StringComparison.Ordinal) => "dotnet/src/Rea.Managed/ManagedStreamHeaders.cs",
        _ when path.StartsWith("src/dotnet/ManagedArtifact", StringComparison.Ordinal) => "dotnet/src/Rea.Managed/ManagedArtifactInspector.cs",
        _ => null
    };
    private static async Task<string> Git(string root, params string[] args)
    {
        var result = await ExecutionChecks.Run(root, "git", new[] { "-c", "core.quotepath=false" }.Concat(args).ToArray());
        if (result.ExitCode != 0) throw new InvalidDataException("Cannot inventory upstream Git history: " + result.Error);
        return result.Output;
    }
}

