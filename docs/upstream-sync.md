# Upstream REA synchronization

The upstream source range starts inclusively at
`b3b5c8b1334cae6b5c0644e4c5750a1e075646cd` and is pinned through
`abfccc0683e73ee396087309ab98c78c9f6dde4b`.
It contains 3,338 reachable commits and 6,744 changed paths when renames are
represented as separate delete/add entries.
This is an inventory and an initial semantic-port batch, **not a complete sync**.

The [machine-readable inventory](upstream-sync.json) enumerates every commit
and changed path with pending review status. It records C# source hashes and
the [specific partial ports](../scripts/upstream-ports.json). The inventory
does not mark an entire upstream commit as completed merely because one behavior
was ported, and it does not import or execute upstream JavaScript or npm hooks.

## Implemented C# behaviors

- Strict UTF-8 decoding for metadata version strings, including valid multibyte
  text and file-offset diagnostics on invalid strings.
- Qualcomm Hexagon ELF machine identification for both byte orders and ELF classes.
- Thin arm64_32 Mach-O architecture identification.
- Mach-O load-command count, size, range and width-specific alignment checks
  within the retained header prefix. Unretained command headers remain unexamined.
- JVM interface indexes must identify class constants before CAFEBABE is accepted
  as a Java class instead of a FAT candidate.
- DOS relocation-table padding does not invent a Windows new-header declaration.
  Unconfirmed candidates and unread signatures remain explicit.

These are static C# inspection changes with controlled C# regression fixtures.
No live Ghidra, Hopper, IDA, Kuna, Apple compiler or other provider verification
is established. The original starting commit fixes a Kuna capability absent from
REA-Sharp; that provider integration remains pending.

## Remaining scope

Upstream includes MCP, setup, provider, browser, desktop, artifact, process and
other capabilities removed from REA-Sharp. Porting all of them is substantial
implementation work, not a Git merge. Node/npm manifests, JavaScript and
TypeScript implementations must not be copied into the C# application.

The confirmed scope includes restoring missing upstream capabilities in C#.
This remains a broad port, not a completed synchronization. A C# stdio MCP adapter
and additive JSON configuration setup now expose the two implemented inspections;
the other providers, client integrations and desktop workflows remain pending.

The website is now built with C# as static content. Executable inline scripts,
event handlers, fourteen JavaScript files and the old npm example manifest were
retired under the zero-JavaScript requirement. Page content and styles remain.
Browser/Electron demos are explicitly unavailable, not claimed as C# equivalents.
See [website recovery records](../website/csharp-migration.json).
Native C++ and the pre-existing Python CTF script were not converted in this batch.

## Reproduce the inventory

Fetch upstream history, build the root solution, then write to a new output path:

```text
git fetch https://github.com/morluto/rea.git main
dotnet build Rea.slnx --configuration Release
dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll upstream-inventory . b3b5c8b1334cae6b5c0644e4c5750a1e075646cd abfccc0683e73ee396087309ab98c78c9f6dde4b NEW_INVENTORY_JSON
```

The C# inventory tool reads Git metadata, validates the ancestry and immutable
commit identities, inventories all commits and net changed paths, and writes a
new JSON file. Existing inventories are never overwritten. Review the pending
entries and add port-specific C# behavior tests before recording further progress.
