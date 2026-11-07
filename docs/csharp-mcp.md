# C# MCP and client configuration

The C# CLI now provides a local stdio MCP adapter for its two implemented static
analysis operations. It does not restore the full upstream provider catalog.

## Run

```text
dotnet build Rea.slnx --configuration Release
dotnet exec dotnet/src/Rea.Cli/bin/Release/net10.0/rea.dll mcp
```

The server reads newline-delimited UTF-8 JSON-RPC on stdin and writes protocol
messages only on stdout. It negotiates MCP 2025-11-25 or 2025-06-18, supports
initialization, ping, tools/list and tools/call, and names the tools
`inspect_header` and `inspect_managed_artifact`. Tools share the CLI workflows;
caller paths, digest scope, coverage, explicit unknowns and limitations are retained.
Inspection failures are tool errors; invalid contracts are JSON-RPC errors.

The header tool accepts `path`, optional `host_architecture`, `header_bytes`
and `format` (dos-com). The managed tool accepts `path` and optional
`max_file_bytes`. These arguments correspond to the existing CLI options.

This initial adapter processes requests serially with bounded input and output
representation sizes. It does not advertise HTTP, retained Evidence sessions,
MCP tasks, dynamic catalogs, notifications about catalog changes or the other
upstream providers. Live agent-client interoperability remains unverified;
regressions use an actual C# server subprocess as well as malformed requests.

Protocol references: [stdio transport](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports),
[lifecycle](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle),
and [tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools).

## Plan and approve JSON configuration

```text
dotnet exec dotnet/src/Rea.Cli/bin/Release/net10.0/rea.dll setup --config /path/to/client.json --dry-run
dotnet exec dotnet/src/Rea.Cli/bin/Release/net10.0/rea.dll setup --config /path/to/client.json --yes
```

The default is plan-only. An explicit --yes approves adding an mcpServers.rea-sharp
entry using dotnet, the current absolute CLI assembly path, and the mcp command.
Existing unrelated JSON properties and server entries remain intact. Existing
rea-sharp entries must already match or setup refuses to replace them. Every
changed existing file gets an exact backup; repeated matching setup does nothing.

This targets clients that use the JSON mcpServers convention. It does not auto-detect
clients, rewrite TOML configuration, register every upstream client, install
providers, install or upgrade runtimes, modify PATH, or invoke Node/npm. The CLI
assembly location must stay available. Setup takes an exclusive .NET file lease;
this does not establish cross-platform coordination with writers that ignore it.
