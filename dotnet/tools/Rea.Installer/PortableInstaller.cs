using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Rea.Installer;

public sealed record ReleaseFile(
    [property: JsonRequired] string Path,
    [property: JsonRequired] long Length,
    [property: JsonRequired] string Sha256);
public sealed record ReleaseManifest(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string Version,
    [property: JsonRequired] string RuntimeIdentifier,
    [property: JsonRequired] string EntryPoint,
    [property: JsonRequired] string SourceRevision,
    [property: JsonRequired] bool SourceDirty,
    [property: JsonRequired] string TestStatus,
    [property: JsonRequired] ReleaseFile[] Files);

public static class PortableInstaller
{
    public const string ResourceName = "Rea.Payload.zip";
    public const string ManifestName = "release-manifest.json";
    public static JsonSerializerOptions JsonOptions { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private const long MaximumBytes = 512L * 1024 * 1024;
    public static async Task<int> RunAsync(Stream payload, string[] args, TextReader input, TextWriter output, TextWriter error)
    {
        string? staging = null;
        try
        {
            var dry = false; var yes = false; string? destination = null; var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index++)
            {
                var option = args[index];
                if (!seen.Add(option)) throw new ArgumentException("Duplicate option: " + option);
                switch (option)
                {
                    case "--help" or "-h":
                        await output.WriteLineAsync("REA-Sharp Setup\nUsage: Setup.exe [--install-dir DIRECTORY] [--dry-run] [--yes]\nInstalls the embedded self-contained Windows CLI per user. No .NET SDK/runtime download, PATH edits, provider installation or client setup."); return 0;
                    case "--dry-run": dry = true; break;
                    case "--yes": yes = true; break;
                    case "--install-dir":
                        if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("--install-dir requires a directory");
                        destination = Path.GetFullPath(args[index]); break;
                    default: throw new ArgumentException("Unknown installer option: " + option);
                }
            }
            using var archive = OpenPayload(payload);
            var manifestBytes = ReadManifestBytes(archive);
            var manifest = Validate(archive, manifestBytes);
            var host = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            if (!OperatingSystem.IsWindows() || manifest.RuntimeIdentifier != host) throw new InvalidDataException("This installer requires " + manifest.RuntimeIdentifier);
            destination ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "REA-Sharp", manifest.Version, manifest.RuntimeIdentifier);
            destination = Path.GetFullPath(destination);
            if (new DirectoryInfo(destination).Parent is null) throw new ArgumentException("Cannot install into a filesystem root");
            NoDirectoryLinks(destination);
            await output.WriteLineAsync($"REA-Sharp installation plan\n  Version: {manifest.Version}\n  Platform: {manifest.RuntimeIdentifier}\n  Directory: {destination}\n  Files: {manifest.Files.Length}\n  .NET runtime: included\n  Full test suite: {manifest.TestStatus}\n  No PATH or client configuration changes.");
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            {
                if (!Matches(destination, manifest, manifestBytes)) throw new IOException("Destination contains a different installation or other files. Choose a new --install-dir; existing files will not be overwritten.");
                await output.WriteLineAsync("This exact release is already installed; no changes made."); return 0;
            }
            if (File.Exists(destination)) throw new IOException("Installation directory is a file");
            if (dry) { await output.WriteLineAsync("Dry run complete; no installation changes made."); return 0; }
            if (!yes)
            {
                await output.WriteLineAsync("Type yes to install:");
                if (await input.ReadLineAsync() != "yes") { await output.WriteLineAsync("Installation cancelled; no changes made."); return 0; }
            }
            var parent = Path.GetDirectoryName(destination)!; Directory.CreateDirectory(parent);
            staging = Path.Combine(parent, ".rea-sharp-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            foreach (var file in manifest.Files)
            {
                var target = Target(staging, file.Path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var source = archive.GetEntry(file.Path)!.Open();
                await using (var targetFile = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await CopyExact(source, targetFile, file.Length);
                if (FileHash(target, file.Length) != file.Sha256) throw new InvalidDataException("Payload changed during installation: " + file.Path);
            }
            await File.WriteAllBytesAsync(Path.Combine(staging, ManifestName), manifestBytes);
            NoDirectoryLinks(destination);
            // Only an empty selected directory can be removed, never its contents.
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: false);
            Directory.Move(staging, destination); staging = null;
            await output.WriteLineAsync($"Installed REA-Sharp {manifest.Version}.\nRun: \"{Path.Combine(destination, manifest.EntryPoint)}\" --help\nUse that executable for MCP registration; no global dotnet installation is required."); return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            await error.WriteLineAsync("Installation failed: " + ex.Message);
            if (staging is not null) await error.WriteLineAsync("Partial installation retained for inspection: " + staging);
            return 1;
        }
    }
    public static ZipArchive OpenPayload(Stream payload)
    {
        if (!payload.CanSeek || payload.Length > 256L * 1024 * 1024) throw new InvalidDataException("Installer payload must be seekable and at most 256 MiB");
        return new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);
    }
    public static byte[] ReadManifestBytes(ZipArchive archive)
    {
        var entry = archive.GetEntry(ManifestName) ?? throw new InvalidDataException("Release manifest missing");
        if (entry.Length > 1024 * 1024) throw new InvalidDataException("Release manifest exceeds 1 MiB");
        using var input = entry.Open(); var bytes = new byte[(int)entry.Length]; input.ReadExactly(bytes);
        if (input.ReadByte() != -1) throw new InvalidDataException("Manifest exceeds its declared length");
        return bytes;
    }
    public static ReleaseManifest Validate(ZipArchive archive, byte[] manifestBytes)
    {
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(manifestBytes, JsonOptions) ?? throw new InvalidDataException("Invalid release manifest");
        if (manifest.SchemaVersion != 1 || manifest.RuntimeIdentifier is not ("win-x64" or "win-arm64") || manifest.EntryPoint != "rea.exe" ||
            !Regex.IsMatch(manifest.Version ?? "", @"\A[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ||
            !Regex.IsMatch(manifest.SourceRevision ?? "", @"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ||
            manifest.TestStatus is not ("passed" or "not-run") || manifest.Files is not { Length: > 0 })
            throw new InvalidDataException("Unsupported release identity, platform or verification metadata");
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            _ = Target(Path.GetFullPath("release-validation"), entry.FullName);
            if (!entries.Add(entry.FullName)) throw new InvalidDataException("Duplicate archive path");
        }
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var file in manifest.Files)
        {
            if (file is null || file.Path == ManifestName || !files.Add(file.Path)) throw new InvalidDataException("Duplicate/reserved payload path");
            _ = Target(Path.GetFullPath("release-validation"), file.Path);
            var entry = archive.GetEntry(file.Path) ?? throw new InvalidDataException("Missing payload file: " + file.Path);
            if (file.Length < 0 || file.Length > MaximumBytes || entry.Length != file.Length || total > MaximumBytes - file.Length)
                throw new InvalidDataException("Payload size exceeds the 512 MiB expansion budget or differs from its manifest");
            total += file.Length;
            if (!Regex.IsMatch(file.Sha256 ?? "", @"\A[0-9a-f]{64}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) throw new InvalidDataException("Invalid file digest");
            using var content = entry.Open();
            if (Hash(content, file.Length) != file.Sha256) throw new InvalidDataException("Payload digest mismatch: " + file.Path);
        }
        if (archive.Entries.Count != manifest.Files.Length + 1) throw new InvalidDataException("Unlisted archive entry");
        foreach (var required in new[] { "rea.exe", "rea.dll", "rea.runtimeconfig.json", "coreclr.dll", "hostfxr.dll", "System.Private.CoreLib.dll" })
            if (!files.Contains(required)) throw new InvalidDataException("Missing self-contained dependency: " + required);
        return manifest;
    }
    public static string Target(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') || relative.StartsWith('/') ||
            relative.Split('/').Any(part => part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(character => character < 32 || "<>\\\"|?*".Contains(character)) ||
                Regex.IsMatch(part, @"\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))))
            throw new InvalidDataException("Unsafe archive path: " + relative);
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Archive path escapes installation directory");
        return target;
    }
    private static bool Matches(string root, ReleaseManifest manifest, byte[] bytes)
    {
        if (!File.Exists(Path.Combine(root, ManifestName)) || !File.ReadAllBytes(Path.Combine(root, ManifestName)).SequenceEqual(bytes)) return false;
        var expected = manifest.Files.Select(file => file.Path).Append(ManifestName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var path in Directory.GetFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path);
                else found.Add(Path.GetRelativePath(root, path).Replace('\\', '/'));
            }
        }
        if (!found.SetEquals(expected)) return false;
        return manifest.Files.All(file => new FileInfo(Target(root, file.Path)).Length == file.Length &&
            FileHash(Target(root, file.Path), file.Length) == file.Sha256);
    }
    private static string FileHash(string path, long length) { using var stream = File.OpenRead(path); return Hash(stream, length); }
    private static string Hash(Stream stream, long length)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[81920]; long read = 0; int count;
        while ((count = stream.Read(buffer)) != 0)
        {
            if (count > length - read) throw new InvalidDataException("File expands beyond its declared length");
            hash.AppendData(buffer, 0, count); read += count;
        }
        if (read != length) throw new InvalidDataException("File length differs from its manifest");
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static async Task CopyExact(Stream input, Stream output, long length)
    {
        var buffer = new byte[81920]; long written = 0; int count;
        while ((count = await input.ReadAsync(buffer)) != 0)
        {
            if (count > length - written) throw new InvalidDataException("File expands beyond its declared length");
            await output.WriteAsync(buffer.AsMemory(0, count)); written += count;
        }
        if (written != length) throw new InvalidDataException("Truncated file payload");
    }
    private static void NoDirectoryLinks(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Installation through directory links is not supported: " + directory.FullName);
    }
}
