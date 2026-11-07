# Repository Guidelines

## Synchronizing with original REA

### Scope and durable state

The user has authorized restoring missing upstream capabilities in C#, including
providers, MCP, setup and desktop analysis. Do not narrow synchronization to the
two original inspection commands or ask again whether missing capabilities belong
in scope. Honor later user changes and resolve genuinely new authority questions
separately.

- Upstream: https://github.com/morluto/rea, branch `main`. Keep this fork's
  `origin` unchanged; fetching upstream does not authorize pushing anywhere.
- Original starting commit, **inclusive**:
  `b3b5c8b1334cae6b5c0644e4c5750a1e075646cd`.
- Last inventoried upstream target:
  `abfccc0683e73ee396087309ab98c78c9f6dde4b`. This is an inspected checkpoint,
  **not** a fully synchronized commit. It must not become the starting point
  for all remaining ports: earlier pending changes still need implementation.
- Read [sync status](docs/upstream-sync.md),
  [the inventory](docs/upstream-sync.json) and
  [behavior-level port records](scripts/upstream-ports.json) first. Prefer their
  current pinned IDs and explicit evidence over this historical checkpoint.
  The initial inventory covers 3,338 commits; most capabilities remain pending.

Application, orchestration and validation implementations must use C#/.NET 10.
Do not merge/cherry-pick upstream implementation wholesale, restore npm manifests,
run upstream npm hooks, or emit executable JavaScript from C# strings. The existing
engine-required Python/Java/Swift exception in `bridge/` remains narrowly scoped.
Keep documentation, configuration, static HTML/CSS and other data in appropriate
formats. Do not delete support folders to satisfy the language policy.
JavaScript-only browser/Electron demos have been explicitly retired, not ported;
their recovery records are in `website/csharp-migration.json`.

### Efficient sync loop

1. Locate the actual checkout root with `git rev-parse --show-toplevel`, inspect
   `git status --short` and preserve existing edits. Use repository-relative paths;
   the checkout was renamed from `rea` to `rea-sharp`.
2. Fetch once per sync session and pin the resulting SHA immediately. Do not
   repeatedly fetch while reviewing one pinned batch:

   ```text
   git fetch https://github.com/morluto/rea.git main
   git rev-parse FETCH_HEAD
   git merge-base --is-ancestor ORIGINAL_START PINNED_TARGET
   ```

   Replace placeholders with verified commit IDs. Do not rely on FETCH_HEAD
   after another fetch. If ancestry changed, investigate instead of resetting
   the fork or silently selecting a new baseline.
3. Reuse the existing inventory for the original backlog. For a newer target,
   first inspect `git log OLD_TARGET..NEW_TARGET` and
   `git diff --name-status OLD_TARGET NEW_TARGET`. This delta is additional
   review work; it does not replace the older pending backlog.
   Refresh the full inventory once per newly selected target, not per fix:

   ```text
   dotnet build Rea.slnx --configuration Release
   dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll upstream-inventory . ORIGINAL_START PINNED_TARGET NEW_INVENTORY_JSON
   ```

   The tool's start is inclusive and it writes only a new output file. Preserve
   earlier snapshots. Regeneration initializes pending statuses; do not use it
   to overwrite manually reviewed progress or claim completed implementation.
4. Select a small, dependency-cohesive capability or fix. Use scoped `git log`,
   `git show COMMIT -- PATHS...` and parent/target diffs. Read the producing
   boundary, affected consumers and upstream tests together; avoid dumping the
   entire inventory or unrelated source into context. Group superseded fixes
   around the current intended behavior while retaining their provenance.
5. Port behavior into its C# owner, then adapt CLI/MCP contracts and fixtures.
   Reuse .NET parsers and shared workflows; preserve caller choices, evidence,
   unknowns, coverage, cancellation, ownership and deterministic failure reasons.
   Build foundational contracts/resources before dependent providers. In
   particular, the original starting commit's Kuna fix is still pending because
   the Kuna integration is absent. An owner-path hint or unsupported-feature
   placeholder is not an implementation.
6. Add meaningful C# regressions, preferably exercising public workflows and
   real compiled fixtures. Test relevant negative paths. Do not translate mocks
   without checking their admission requirements, treat simulated providers as
   real-engine evidence, or repair failures by suppressing assertions.
7. Update the behavior-level port record with source SHA(s), C# owners, behavior,
   actual verification and remaining limitations. Distinguish pending,
   implemented-but-unverified, verified in the stated scope, and explicitly
   retired/inapplicable work. A partial behavior port does not complete an
   entire upstream commit. Update current documentation and inventory snapshots
   when their recorded source hashes or scope change.

### Validation and handoff

Use focused checks while iterating; run the relevant complete gates at a coherent
batch boundary. Avoid repeated unchanged full-suite runs. Build the solution and
use the dependency-free test executable; `dotnet test` does not run its assertions.

```text
dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll repository .
dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll test-hygiene . dotnet/artifacts/reports/test-results.json
dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll website . dotnet/artifacts/static-website
dotnet pack dotnet/src/Rea.Cli --configuration Release --no-build --output dotnet/artifacts/packages
dotnet exec dotnet/tests/Rea.Tests/bin/Release/net10.0/Rea.Tests.dll --verify-packages dotnet/artifacts/packages
dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll package . dotnet/artifacts/packages
git diff --check
```

Scan for remaining JS/TS source and executable HTML/SVG scripts as well as Node/npm
references in active workflows and package contents. Keep generated artifacts
under ignored output paths. Do not overwrite retained measurements or source
inventories. Inspect dirty changes before running commit-qualified release checks.

The last recorded batch passed 182/183 tests; its source-policy failure identified
`.gitmodules`, native C++ and the Python CTF example. This is historical evidence,
not a permanent expected-failure exemption: rerun relevant checks, report the
current failures and do not weaken the policy to turn the suite green. See
[verification](docs/testing.md), [C# MCP/setup](docs/csharp-mcp.md) and
[validation tooling](scripts/README.md).

Report the pinned upstream range, completed behaviors, actual checks and failures,
retired functionality and pending capabilities. Inventory completion is not port
completion; local fixtures are not live provider/client or remote CI verification.
Do not claim full synchronization while pending work remains, and do not commit,
push or publish unless separately authorized.

## Implementation and development

The application, repository automation and experiments use C# targeting .NET 10.
Do not add TypeScript, JavaScript, shell-script implementations or engine-side
JavaScript. The user explicitly authorized an exception for restored engine-required
Python, Java and Swift sources in `bridge/`; preserve that exception without treating
it as permission to add other-language application/orchestration code. Bridge sources
are not enabled C# providers. Configuration, documentation and source assets may use
JSON, XML, YAML, Markdown, Mermaid, CSV, SVG, PNG and plain text. Do not restore npm
manifests or workflows that require the removed Node runtime.

Use the SDK selected by `global.json`. From the repository root:

- `dotnet build Rea.slnx --configuration Release`
- `dotnet run --project dotnet/tests/Rea.Tests --configuration Release --no-build`
- `dotnet pack dotnet/src/Rea.Cli --configuration Release --no-build --output dotnet/artifacts/packages`
- `dotnet exec dotnet/tests/Rea.Tests/bin/Release/net10.0/Rea.Tests.dll --verify-packages dotnet/artifacts/packages`

The tests are a dependency-free executable. `dotnet test` does not execute these
assertions. Test suites use one `*Tests.cs` file per suite and a public static
`IEnumerable<Case> Cases()` method. Discovery is automatic and checks the source
inventory; only PackageTests takes a directory argument. See scripts/README.md
for C# architecture, documentation/skill, cleanup and installed-package gates.
Repository policy tests reject non-C# source and obsolete build manifests.

## Product and authority

Current supported operations are documented in README.md. Do not infer support
from historical changelogs or removed interfaces. Distinguish observations,
derivations, inferences, and unknowns. Never claim a complete feature-equivalent
port, real-provider verification, or a test result without evidence.

Follow the contribution scope and fail-closed integration rule in CONTRIBUTING.md.
New providers or capabilities involving target execution, injection, mutation,
or protection recovery need an explicit maintainer scope decision before
substantial implementation intended for integration. Exploratory prototypes do
not authorize integration. Do not save or close an attached IDA GUI database.

Preserve caller-selected values, paths, digests, metadata, and local evidence.
Redact transport credentials and explicitly marked sensitive values; do not infer
secrecy from names. Do not persist an inherited ambient environment. Configuration
changes must be additive and idempotent with backups, and setup must show planned
changes and require approval before writing or installing anything.

## Architecture and ownership

Domain semantics and Result contracts are inward dependencies. Application
workflows own target snapshots and file identity. Provider adapters own SDK and
format interpretation; CLI adapters perform input/output translation. See
docs/architecture.mermaid. Keep providers and protocols out of domain/application
code. Bind operations to named, exactly typed contracts.

Give each interpretation and resource one owner. Normalize once at that boundary
and share its parsed facts with consumers. Preserve omission and explicit unknowns;
unexamined data is not absence. Keep proof of ownership separate from validity,
editability, and readiness. Retain ownership after failed cleanup. A PID and path
alone cannot establish process ownership after exit or reuse.

Use tagged Result variants for expected failures. Check lengths, allocation and
representation expansion before expensive work. Preserve admitted partial facts
and meaningful failure reasons. When fixing bugs or review findings, inspect the
producing boundary and consumers, complete related structural cleanup, remove
superseded representations and redundant tests, and explain remaining limitations.

## Verification and performance

Prefer public CLI workflows, then production-boundary integration, then distinct
module semantics. Preserve malformed input, cancellation, capacity, permissions,
cleanup and target identity coverage. Real-provider claims need real-provider
evidence. Validate advertised schemas against their dialect and actual clients
if an MCP adapter is introduced. See docs/testing.md and docs/tool-design.md.

Optimize agent context first, analysis time second, CPU and memory third. Preserve
correctness, provenance, caller choices and bounded resources. Measure equivalent
completed workflows where available; report token usage, elapsed/CPU time and
peak process-family memory, label cold/warm runs and estimates, and disclose
unmeasured costs. Fewer bytes alone do not establish fewer tokens or faster work.

## Changes and review

Use Conventional Commit subjects and PR titles, marking breaking changes with `!`
or a BREAKING CHANGE footer. Do not commit assemblies, packages, provider projects,
credentials, build outputs or local planning artifacts. Keep PR bodies concise
and current with behavior, checks that actually ran and verification gaps.
Address actionable automated findings and explain disagreements.

A human must review and own the final diff and declare it ready for initial
maintainer review. Leave Author readiness declarations unchecked for the human;
keep a PR in draft until that declaration. Do not attest to human review or
interpret tests as maintainer scope approval or merge permission.
