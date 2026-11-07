using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rea.Cli;

/// <summary>Local stdio MCP adapter for implemented static inspections; no Node runtime.</summary>
public static class McpServer
{
    private const int InputCharacters = 1024 * 1024;
    private const long OutputBytes = 64L * 1024 * 1024;
    private const string Protocol = "2025-11-25";
    public static async Task<int> RunAsync(TextReader input, TextWriter output, TextWriter diagnostics, CancellationToken cancellation = default)
    {
        var reader = new MessageReader(input); var initialized = false; var ready = false;
        try
        {
            while (await reader.Read(cancellation) is { } line)
            {
                JsonNode? id = null; JsonObject? response = null;
                try
                {
                    var request = JsonNode.Parse(line) as JsonObject ?? throw new RpcException(-32600, "Expected one JSON-RPC object");
                    var method = request["method"]?.GetValue<string>() ?? throw new RpcException(-32600, "Request method missing");
                    if (request["jsonrpc"]?.GetValue<string>() != "2.0") throw new RpcException(-32600, "Expected JSON-RPC 2.0");
                    var hasId = request.TryGetPropertyValue("id", out id);
                    if (hasId && (id is null || id.GetValueKind() is not (JsonValueKind.String or JsonValueKind.Number))) { id = null; throw new RpcException(-32600, "Invalid request ID"); }
                    if (hasId && id!.GetValueKind() == JsonValueKind.Number && !long.TryParse(id.ToJsonString(), out _)) throw new RpcException(-32600, "Request ID must be an integer or string");
                    if (!hasId)
                    {
                        if (method == "notifications/initialized" && initialized) ready = true;
                        continue;
                    }
                    JsonNode result;
                    switch (method)
                    {
                        case "initialize":
                            if (initialized) throw new RpcException(-32600, "Session already initialized");
                            var parameters = request["params"] as JsonObject ?? throw new RpcException(-32602, "Initialize parameters required");
                            if (parameters["protocolVersion"]?.GetValue<string>() is not { Length: > 0 } || parameters["clientInfo"] is not JsonObject client || client["name"]?.GetValue<string>() is not { Length: > 0 } || client["version"]?.GetValue<string>() is not { Length: > 0 } || parameters["capabilities"] is not JsonObject)
                                throw new RpcException(-32602, "Protocol version, capabilities and named/versioned client required");
                            initialized = true;
                            var requested = parameters["protocolVersion"]!.GetValue<string>();
                            result = new JsonObject
                            {
                                ["protocolVersion"] = requested is "2025-06-18" or "2025-11-25" ? requested : Protocol,
                                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                                ["serverInfo"] = new JsonObject { ["name"] = "rea-sharp", ["version"] = CliApplication.Version },
                                ["instructions"] = "Static C# inspections only. Preserve reported digest scope, limitations and unknowns. The legacy provider/MCP catalog is not restored."
                            }; break;
                        case "ping": result = new JsonObject(); break;
                        case "tools/list":
                            if (!ready) throw new RpcException(-32002, "Complete MCP initialization first");
                            if (request["params"] is JsonObject paging && paging["cursor"] is not null) throw new RpcException(-32602, "This complete two-tool catalog has no continuation cursor");
                            result = new JsonObject { ["tools"] = Catalog() }; break;
                        case "tools/call":
                            if (!ready) throw new RpcException(-32002, "Complete MCP initialization first");
                            result = await Call(request["params"] as JsonObject ?? throw new RpcException(-32602, "Tool call parameters required"), cancellation); break;
                        default: throw new RpcException(-32601, "Method not supported: " + method);
                    }
                    response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result };
                }
                catch (RpcException ex) { response = Error(id, ex.Code, ex.Message); }
                catch (JsonException) { response = Error(null, -32700, "Invalid JSON"); }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException) { response = Error(id, -32602, "Invalid parameter type"); }
                if (response is not null)
                {
                    var serialized = response.ToJsonString();
                    if (Encoding.UTF8.GetByteCount(serialized) > OutputBytes) serialized = Error(id, -32603, "Response exceeds transport capacity; reduce inspection budgets or use the CLI").ToJsonString();
                    await output.WriteLineAsync(serialized); await output.FlushAsync(cancellation);
                }
            }
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return 130; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or DecoderFallbackException)
        { await diagnostics.WriteLineAsync("MCP transport failed: " + ex.Message); return 1; }
    }
    private static async Task<JsonNode> Call(JsonObject parameters, CancellationToken cancellation)
    {
        var name = parameters["name"]?.GetValue<string>() ?? throw new RpcException(-32602, "Tool name required");
        var arguments = parameters["arguments"] as JsonObject ?? throw new RpcException(-32602, "Tool arguments required");
        var command = name switch { "inspect_header" => "inspect-header", "inspect_managed_artifact" => "inspect-managed-artifact", _ => throw new RpcException(-32602, "Unknown tool: " + name) };
        var path = arguments["path"]?.GetValue<string>();
        if (string.IsNullOrEmpty(path)) throw new RpcException(-32602, "A nonempty path is required");
        var args = new List<string> { command, path };
        foreach (var argument in arguments.Where(item => item.Key != "path"))
        {
            var option = (name, argument.Key) switch
            {
                ("inspect_header", "host_architecture") => "--host-architecture",
                ("inspect_header", "header_bytes") => "--header-bytes",
                ("inspect_header", "format") => "--format",
                ("inspect_managed_artifact", "max_file_bytes") => "--max-file-bytes",
                _ => throw new RpcException(-32602, "Unknown tool argument: " + argument.Key)
            };
            if (argument.Value is null) throw new RpcException(-32602, "Null option: " + argument.Key);
            args.Add(option);
            if (argument.Key is "header_bytes" or "max_file_bytes")
            {
                if (argument.Value.GetValueKind() != JsonValueKind.Number || !long.TryParse(argument.Value.ToJsonString(), out var number) || number <= 0) throw new RpcException(-32602, "Positive integer required: " + argument.Key);
                args.Add(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else args.Add(argument.Value.GetValue<string>());
        }
        using var output = new StringWriter(); using var error = new StringWriter();
        var exit = await CliApplication.RunAsync(args.ToArray(), output, error, cancellation);
        if (exit == 2) throw new RpcException(-32602, error.ToString().Trim());
        var text = output.ToString();
        if (text.Length == 0) text = JsonSerializer.Serialize(new { ok = false, error = error.ToString(), exit_code = exit });
        // Account for both JSON text escaping and structured content before allocating envelopes.
        if (text.Length * 12L + 4096 > OutputBytes)
            return new JsonObject { ["isError"] = true, ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Inspection response exceeds MCP representation capacity; use a smaller inspection budget or the CLI. No complete result is claimed." }) };
        return new JsonObject
        {
            ["isError"] = exit != 0,
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["structuredContent"] = JsonNode.Parse(text)
        };
    }
    private static JsonArray Catalog()
    {
        JsonObject String() => new() { ["type"] = "string" };
        JsonObject Integer(long max) => new() { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = max };
        JsonObject Tool(string name, string description, JsonObject properties) => new()
        {
            ["name"] = name, ["description"] = description,
            ["inputSchema"] = new JsonObject { ["$schema"] = "https://json-schema.org/draft/2020-12/schema", ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray("path"), ["additionalProperties"] = false },
            ["annotations"] = new JsonObject { ["readOnlyHint"] = true, ["destructiveHint"] = false, ["idempotentHint"] = true, ["openWorldHint"] = false }
        };
        var path = String(); path["minLength"] = 1;
        var header = new JsonObject { ["path"] = path.DeepClone(), ["header_bytes"] = Integer(Array.MaxLength), ["host_architecture"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("x64", "ia32", "arm64", "arm") }, ["format"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("dos-com") } };
        return new JsonArray(Tool("inspect_header", "Read a bounded executable header. Its digest identifies only the retained prefix; no target code executes.", header),
            Tool("inspect_managed_artifact", "Statically inspect PE/CLI metadata and resource identities. Preserves full-snapshot identity, coverage and explicit unknowns.", new JsonObject { ["path"] = path, ["max_file_bytes"] = Integer(Array.MaxLength) }));
    }
    private static JsonObject Error(JsonNode? id, int code, string message) => new() { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };
    private sealed class RpcException(int code, string message) : Exception(message) { public int Code { get; } = code; }
    private sealed class MessageReader(TextReader input)
    {
        private readonly char[] buffer = new char[4096]; private int offset; private int count;
        public async Task<string?> Read(CancellationToken cancellation)
        {
            var text = new StringBuilder();
            while (true)
            {
                if (offset == count) { count = await input.ReadAsync(buffer.AsMemory(), cancellation); offset = 0; if (count == 0) return text.Length == 0 ? null : text.ToString(); }
                var newline = Array.IndexOf(buffer, '\n', offset, count - offset);
                var take = (newline >= 0 ? newline : count) - offset;
                if (text.Length + take > InputCharacters) throw new InvalidDataException("MCP input exceeds the one Mi-character message budget");
                text.Append(buffer, offset, take); offset += take;
                if (newline >= 0) { offset++; return text.ToString(); }
            }
        }
    }
}
