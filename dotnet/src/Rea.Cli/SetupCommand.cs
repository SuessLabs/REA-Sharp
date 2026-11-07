using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rea.Cli;

/// <summary>Explicit, additive JSON MCP registration. No engines or runtimes are installed.</summary>
public static class SetupCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            string? path = null; var yes = false; var dry = false; var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index++)
            {
                if (!seen.Add(args[index])) throw new ArgumentException("Duplicate setup option: " + args[index]);
                switch (args[index])
                {
                    case "--config":
                        if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("--config requires a JSON path");
                        path = Path.GetFullPath(args[index]); break;
                    case "--yes": yes = true; break;
                    case "--dry-run": dry = true; break;
                    default: throw new ArgumentException("Usage: rea setup --config JSON_PATH [--dry-run] [--yes]");
                }
            }
            if (path is null) throw new ArgumentException("Select an explicit JSON MCP client configuration using --config. No automatic client/engine installation is available.");
            var processPath = Environment.ProcessPath;
            var nativeHost = processPath is not null && string.Equals(Path.GetFileNameWithoutExtension(processPath), "rea", StringComparison.OrdinalIgnoreCase);
            var entry = nativeHost
                ? new JsonObject { ["command"] = processPath, ["args"] = new JsonArray("mcp") }
                : new JsonObject { ["command"] = "dotnet", ["args"] = new JsonArray("exec", typeof(CliApplication).Assembly.Location, "mcp") };
            await output.WriteLineAsync("REA-Sharp setup plan\n  Configuration: " + path + "\n  Add: mcpServers.rea-sharp\n  Entry: " + entry.ToJsonString() + "\n  Existing values are preserved; changed files get an exact backup. No provider, Node/npm, runtime or PATH installation.");
            if (File.Exists(path))
            {
                await using var original = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (original.Length > 4 * 1024 * 1024) throw new InvalidDataException("Client configuration exceeds the 4 MiB budget");
                var prior = new byte[(int)original.Length]; await original.ReadExactlyAsync(prior);
                _ = Prepare(prior, true, entry);
            }
            if (!yes || dry) { await output.WriteLineAsync("Plan only. Pass --yes to approve these changes."); return 0; }
            var parent = Path.GetDirectoryName(path)!; Directory.CreateDirectory(parent);
            var existed = File.Exists(path);
            // Retain an exclusive owner for the read/modify/write boundary.
            await using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (file.Length > 4 * 1024 * 1024) throw new InvalidDataException("Client configuration exceeds the 4 MiB budget");
            var bytes = new byte[(int)file.Length]; await file.ReadExactlyAsync(bytes);
            var prepared = Prepare(bytes, existed, entry);
            if (!prepared.Changed) { await output.WriteLineAsync("Already configured; no changes made."); return 0; }
            var config = prepared.Config;
            var serialized = Encoding.UTF8.GetBytes(config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            string? backup = null;
            if (existed)
            {
                backup = path + ".backup-" + Guid.NewGuid().ToString("N");
                await using var preserved = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await preserved.WriteAsync(bytes); await preserved.FlushAsync();
            }
            try { file.Position = 0; await file.WriteAsync(serialized); file.SetLength(serialized.Length); await file.FlushAsync(); }
            catch { file.Position = 0; await file.WriteAsync(bytes); file.SetLength(bytes.Length); await file.FlushAsync(); throw; }
            await output.WriteLineAsync("Configured REA-Sharp." + (backup is null ? "" : " Backup: " + backup)); return 0;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        { await error.WriteLineAsync("Setup failed: " + ex.Message); return 1; }
    }
    private static (JsonObject Config, bool Changed) Prepare(byte[] bytes, bool existed, JsonObject entry)
    {
        JsonObject config;
        try
        {
            var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            config = bytes.Length == 0 && !existed ? new JsonObject()
                : JsonNode.Parse(bytes.AsSpan(offset)) as JsonObject ?? throw new InvalidDataException("Configuration must be a JSON object");
        }
        catch (JsonException ex) { throw new InvalidDataException("Existing configuration is malformed; no values were changed", ex); }
        JsonObject servers;
        if (config.TryGetPropertyValue("mcpServers", out var existing))
            servers = existing as JsonObject ?? throw new InvalidDataException("Existing mcpServers must be an object");
        else { servers = new JsonObject(); config["mcpServers"] = servers; }
        if (servers.TryGetPropertyValue("rea-sharp", out var registered))
        {
            if (!JsonNode.DeepEquals(registered, entry)) throw new InvalidDataException("Existing rea-sharp entry differs; inspect it explicitly before changing it");
            return (config, false);
        }
        servers["rea-sharp"] = entry.DeepClone();
        return (config, true);
    }

}
