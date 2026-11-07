using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Rea.Installer;

namespace Rea.Tests;

internal static class PortableInstallerTests
{
    private static readonly string[] Dependencies = ["rea.exe", "rea.dll", "rea.runtimeconfig.json", "coreclr.dll", "hostfxr.dll", "System.Private.CoreLib.dll"];
    public static IEnumerable<Case> Cases()
    {
        yield return Case.Sync("Portable installer validates every archived digest and self-contained dependency", () =>
        {
            using var payload = Payload(); using var archive = PortableInstaller.OpenPayload(payload);
            var manifest = PortableInstaller.Validate(archive, PortableInstaller.ReadManifestBytes(archive));
            Check.Equal(Dependencies.Length, manifest.Files.Length);
            foreach (var defect in new[] { "digest", "unlisted", "missing-runtime", "duplicate", "oversized" })
            {
                using var broken = Payload(defect); using var invalid = PortableInstaller.OpenPayload(broken);
                var rejected = false;
                try { PortableInstaller.Validate(invalid, PortableInstaller.ReadManifestBytes(invalid)); } catch (InvalidDataException) { rejected = true; }
                Check.True(rejected, "Invalid payload accepted: " + defect);
            }
        });
        yield return Case.Sync("Portable installer rejects traversal absolute paths and Windows stream aliases", () =>
        {
            foreach (var path in new[] { "../outside", "/absolute", "C:/outside", "a\\b", "a/../b", "a//b", "file:stream", "directory./file", "trailing " })
            {
                var rejected = false; try { PortableInstaller.Target(Path.GetTempPath(), path); } catch (InvalidDataException) { rejected = true; }
                Check.True(rejected, "Unsafe archive path accepted: " + path);
            }
        });
        yield return new(OperatingSystem.IsWindows() ? "Portable Windows installer dry run cancellation installation and idempotence" : "Portable Windows installer refuses incompatible operating systems", async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "rea-release-installer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var target = Path.Combine(root, "installed"); using var payload = Payload();
                async Task<int> Run(string answer, params string[] options)
                {
                    payload.Position = 0;
                    return await PortableInstaller.RunAsync(payload, options, new StringReader(answer), new StringWriter(), new StringWriter());
                }
                if (!OperatingSystem.IsWindows())
                { Check.Equal(1, await Run("", "--install-dir", target, "--yes")); Check.True(!Directory.Exists(target)); return; }
                Check.Equal(0, await Run("", "--install-dir", target, "--dry-run")); Check.True(!Directory.Exists(target));
                Check.Equal(0, await Run("no\n", "--install-dir", target)); Check.True(!Directory.Exists(target));
                Check.Equal(0, await Run("", "--install-dir", target, "--yes"));
                Check.True(File.Exists(Path.Combine(target, "rea.exe")));
                Check.Equal(0, await Run("", "--install-dir", target, "--yes"));
                var changed = Path.Combine(target, "rea.dll"); await File.WriteAllTextAsync(changed, "user-modified");
                Check.Equal(1, await Run("", "--install-dir", target, "--yes")); Check.Equal("user-modified", await File.ReadAllTextAsync(changed));
                var other = Path.Combine(root, "foreign"); Directory.CreateDirectory(other); await File.WriteAllTextAsync(Path.Combine(other, "keep.txt"), "keep");
                Check.Equal(1, await Run("", "--install-dir", other, "--yes")); Check.Equal("keep", await File.ReadAllTextAsync(Path.Combine(other, "keep.txt")));
            }
            finally { Directory.Delete(root, true); }
        });
        yield return new("Portable installer refuses corrupt payloads before changing installation paths", async () =>
        {
            using var payload = Payload("digest"); var target = Path.Combine(Path.GetTempPath(), "rea-refused-" + Guid.NewGuid().ToString("N"));
            Check.Equal(1, await PortableInstaller.RunAsync(payload, ["--install-dir", target, "--yes"], new StringReader(""), new StringWriter(), new StringWriter()));
            Check.True(!Directory.Exists(target));
        });
    }
    // Tiny archives exercise integrity/extraction semantics, not executable-runtime claims.
    // build.ps1 separately installs and runs the real published CLI in isolated staging.
    private static MemoryStream Payload(string? defect = null)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var files = new List<ReleaseFile>();
            foreach (var name in Dependencies.Where(name => defect != "missing-runtime" || name != "coreclr.dll"))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes("fixture-" + name);
                using (var entry = zip.CreateEntry(name).Open()) entry.Write(bytes);
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                files.Add(new(name, defect == "oversized" && name == "rea.exe" ? 513L * 1024 * 1024 : bytes.Length, defect == "digest" && name == "rea.exe" ? new string('0', 64) : hash));
            }
            if (defect is "unlisted" or "duplicate") using (var extra = zip.CreateEntry(defect == "duplicate" ? "REA.EXE" : "unlisted.dll").Open()) extra.WriteByte(1);
            var manifest = new ReleaseManifest(1, "1.2.3", RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64", "rea.exe", new string('a', 40), true, "not-run", files.ToArray());
            using var metadata = zip.CreateEntry(PortableInstaller.ManifestName).Open(); JsonSerializer.Serialize(metadata, manifest, PortableInstaller.JsonOptions);
        }
        stream.Position = 0; return stream;
    }
}
