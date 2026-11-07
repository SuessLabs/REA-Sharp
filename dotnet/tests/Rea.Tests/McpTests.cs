using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Rea.Cli;

namespace Rea.Tests;

internal static class McpTests
{
    private static string Initialize => """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"rea-sharp-test","version":"1"}}}""";
    private static string Ready => """{"jsonrpc":"2.0","method":"notifications/initialized"}""";
    public static IEnumerable<Case> Cases()
    {
        yield return new("C# MCP stdio subprocess initializes lists named tools and inspects a real managed fixture", async () =>
        {
            var fixture = Path.Combine(AppContext.BaseDirectory, "Rea.ManagedFixture.dll");
            var call = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = "managed-call", ["method"] = "tools/call", ["params"] = new JsonObject { ["name"] = "inspect_managed_artifact", ["arguments"] = new JsonObject { ["path"] = fixture } } }.ToJsonString();
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rea.slnx"))) directory = directory.Parent;
            foreach (var arg in new[] { "exec", Path.Combine(directory!.FullName, "dotnet/src/Rea.Cli/bin/Release/net10.0/rea.dll"), "mcp" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!; using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token); var error = process.StandardError.ReadToEndAsync(deadline.Token);
            try
            {
                foreach (var line in new[] { Initialize, Ready, """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""", call }) await process.StandardInput.WriteLineAsync(line);
                process.StandardInput.Close(); await process.WaitForExitAsync(deadline.Token);
                Check.Equal(0, process.ExitCode); Check.Equal("", await error);
                var replies = (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!).ToArray();
                Check.Equal(3, replies.Length); Check.Equal("2025-11-25", replies[0]["result"]!["protocolVersion"]!.GetValue<string>());
                var tools = replies[1]["result"]!["tools"]!.AsArray();
                Check.Equal("inspect_header,inspect_managed_artifact", string.Join(',', tools.Select(tool => tool!["name"]!.GetValue<string>())));
                foreach (var tool in tools) Check.True(!tool!["inputSchema"]!["additionalProperties"]!.GetValue<bool>());
                var result = replies[2]["result"]!; Check.True(!result["isError"]!.GetValue<bool>());
                Check.Equal("managed-call", replies[2]["id"]!.GetValue<string>());
                var structured = result["structuredContent"]!; Check.True(structured["ok"]!.GetValue<bool>());
                Check.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fixture))), structured["value"]!["artifact"]!["sha256"]!.GetValue<string>());
            }
            finally
            {
                if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
                try { await Task.WhenAll(output, error); } catch (OperationCanceledException) { }
            }
        });
        yield return new("C# MCP validates initialization argument types unknown tools and domain failures", async () =>
        {
            var lines = new[]
            {
                """{"jsonrpc":"2.0","id":0,"method":"tools/list"}""",
                Initialize, Ready,
                """{"jsonrpc":"2.0","id":3,"method":"not-a-method"}""",
                """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"unknown","arguments":{"path":"x"}}}""",
                """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"inspect_header","arguments":{"path":"x","header_bytes":1.5}}}""",
                new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 6, ["method"] = "tools/call", ["params"] = new JsonObject { ["name"] = "inspect_managed_artifact", ["arguments"] = new JsonObject { ["path"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".absent") } } }.ToJsonString()
            };
            using var output = new StringWriter(); using var error = new StringWriter();
            Check.Equal(0, await McpServer.RunAsync(new StringReader(string.Join('\n', lines) + "\n"), output, error));
            var replies = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!).ToArray();
            Check.Equal(-32002, replies[0]["error"]!["code"]!.GetValue<int>());
            Check.Equal(-32601, replies[2]["error"]!["code"]!.GetValue<int>());
            Check.Equal(-32602, replies[3]["error"]!["code"]!.GetValue<int>());
            Check.Equal(-32602, replies[4]["error"]!["code"]!.GetValue<int>());
            Check.True(replies[5]["result"]!["isError"]!.GetValue<bool>());
        });
        yield return new("C# MCP rejects malformed JSON and bounds input before allocating an unbounded line", async () =>
        {
            using var output = new StringWriter(); using var error = new StringWriter();
            Check.Equal(0, await McpServer.RunAsync(new StringReader("{broken}\n"), output, error));
            Check.Equal(-32700, JsonNode.Parse(output.ToString())!["error"]!["code"]!.GetValue<int>());
            Check.Equal(1, await McpServer.RunAsync(new StringReader(new string('x', 1024 * 1024 + 1)), new StringWriter(), new StringWriter()));
        });
        yield return new("C# setup plans before writing preserves unrelated values backs up and is idempotent", async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "rea-setup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                var file = Path.Combine(root, "client.json"); var original = """{"other":"keep","mcpServers":{"unrelated":{"command":"keep-me"}}}""";
                await File.WriteAllTextAsync(file, original);
                Check.Equal(0, await SetupCommand.RunAsync(["--config", file, "--dry-run"], new StringWriter(), new StringWriter()));
                Check.Equal(original, await File.ReadAllTextAsync(file)); Check.Equal(1, Directory.GetFiles(root).Length);
                Check.Equal(0, await SetupCommand.RunAsync(["--config", file, "--yes"], new StringWriter(), new StringWriter()));
                var updated = JsonNode.Parse(await File.ReadAllTextAsync(file))!;
                Check.Equal("keep", updated["other"]!.GetValue<string>()); Check.Equal("keep-me", updated["mcpServers"]!["unrelated"]!["command"]!.GetValue<string>());
                Check.Equal(original, await File.ReadAllTextAsync(Directory.GetFiles(root, "*.backup-*").Single()));
                Check.Equal(0, await SetupCommand.RunAsync(["--config", file, "--yes"], new StringWriter(), new StringWriter())); Check.Equal(2, Directory.GetFiles(root).Length);
                await File.WriteAllTextAsync(file, """{"mcpServers":{"rea-sharp":{"command":"foreign"}}}""");
                var foreign = await File.ReadAllTextAsync(file);
                Check.Equal(1, await SetupCommand.RunAsync(["--config", file, "--yes"], new StringWriter(), new StringWriter())); Check.Equal(foreign, await File.ReadAllTextAsync(file));
            }
            finally { Directory.Delete(root, true); }
        });
    }
}
