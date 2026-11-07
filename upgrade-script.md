# REA-Sharp upgrade prompt

Copy the prompt below into an AI coding session opened at this repository's root.
It requests implementation, not just a comparison or migration plan. Repository
files are the durable progress record; the original starting commit stays fixed.

## Copy-and-paste prompt

```text
Synchronize REA-Sharp with https://github.com/morluto/rea, including the backlog
starting INCLUSIVELY at b3b5c8b1334cae6b5c0644e4c5750a1e075646cd and all relevant
changes through a freshly fetched, immutable upstream main commit.

Do the implementation and verification. The scope includes restoring missing
upstream capabilities in C#, not merely updating the features already implemented.
Providers, MCP, setup, desktop analysis, CLI workflows, automation and experiments
are in scope. Do not stop at an inventory or describe an initial batch as a full
synchronization.

Non-negotiable constraints:
- Application, orchestration, validation and experiment implementations use C#
  targeting .NET 10. No JavaScript, TypeScript, Node/npm runtime, npm manifests,
  executable browser scripts or JavaScript embedded in C# strings.
- The user-approved root build.bat/build.ps1 files are Windows build orchestration
  exceptions. Preserve them; installer and application logic remain C#.
- Preserve the existing narrow exception for engine-required Python/Java/Swift
  sources in bridge/. It does not authorize other-language application code.
- Keep documentation, configuration, static HTML/CSS and data assets as appropriate.
  Preserve support folders and user changes. Do not delete capabilities or tests
  just to satisfy a language check. Previously retired browser/Electron demos
  remain explicitly retired until a working C# replacement exists.
- Port observable behavior and contracts; do not mechanically merge upstream
  implementation, introduce fake providers, or equate a placeholder with a port.
- Do not commit, push, publish, install engines or modify real client configuration
  without the applicable authorization. Use isolated fixtures for verification.

Establish the exact state:
1. Locate the checkout with git rev-parse --show-toplevel and inspect git status
   --short. Preserve unrelated and uncommitted work.
2. Read AGENTS.md, docs/upstream-sync.md, scripts/upstream-ports.json and the
   relevant parts of docs/upstream-sync.json. Also consult docs/testing.md,
   scripts/README.md and docs/csharp-mcp.md as needed.
3. Fetch upstream main once:
     git fetch https://github.com/morluto/rea.git main
     git rev-parse FETCH_HEAD
   Record the full target SHA immediately and verify ancestry from the original
   starting commit. Keep this fork's origin unchanged. Do not rely on FETCH_HEAD
   after another fetch or reset the fork when ancestry is unexpected.
4. Distinguish the last INVENTORIED target from completed behavior ports. An
   inspected checkpoint is not permission to skip earlier pending commits.
   For a newer target, inspect OLD_TARGET..NEW_TARGET as additional work while
   retaining the original backlog.

Work efficiently and continue implementing:
5. Reuse the inventory instead of dumping it into context or recreating it per
   fix. Scope git log, git show and diffs to a dependency-cohesive capability.
   Inspect the producer, consumers and upstream regressions together. Account
   for fixes superseded by later commits without losing their provenance.
6. Build domain contracts, error/provenance models and resource ownership before
   dependent providers and adapters. Preserve caller choices, exact identities,
   explicit unknowns, coverage, partial facts, cancellation and cleanup semantics.
   Reuse existing C# owners and .NET parsers rather than duplicating interpretation.
7. Implement the capability in C#, integrate its public CLI/MCP workflows, and add
   meaningful C# regression checks. Cover relevant malformed inputs, failures,
   capacity, cancellation, ownership and cleanup. Never execute inspected targets
   merely to validate static inspection. Distinguish fake-engine coverage from
   actual provider verification.
8. Finish each coherent batch and proceed to the next pending dependency without
   repeatedly asking whether missing capabilities are in scope. Ask only when
   genuinely missing information or a new authority decision blocks the work.
   If a feature requires forbidden JavaScript, record that incompatibility and
   its consequences explicitly; do not hide scripts or advertise unsupported work.

Verify using the actual C# gates:
- Build: dotnet build Rea.slnx --configuration Release
- Repository: dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll repository .
- Tests/cleanup: dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll test-hygiene . dotnet/artifacts/reports/test-results.json
- Website: dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll website . dotnet/artifacts/static-website
- Package: dotnet pack dotnet/src/Rea.Cli --configuration Release --no-build --output dotnet/artifacts/packages
- Archive checks: dotnet exec dotnet/tests/Rea.Tests/bin/Release/net10.0/Rea.Tests.dll --verify-packages dotnet/artifacts/packages
- Installed workflows: dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll package . dotnet/artifacts/packages
- Diff: git diff --check

Use focused checks during iteration and the applicable complete gates at batch
boundaries. dotnet test does not run this repository's custom assertions. Inspect
existing packages before reusing an output directory; stale or multiple packages
must not select the wrong artifact. Do not weaken checks or exempt historical
failures to make the suite green. Scan active sources, HTML/SVG, workflows and
package contents for forbidden JavaScript/TypeScript/Node dependencies. Report
unavailable engine/client coverage honestly and preserve existing evidence.

Maintain resumable, accurate progress:
- Record exact upstream SHA(s), C# owners, implemented behavior, executed
  verification and limitations in scripts/upstream-ports.json and sync docs.
- Distinguish pending, implemented-but-unverified, verified in a stated scope,
  and explicitly retired/inapplicable work. A partial port does not complete
  an entire upstream commit.
- Refresh the full inventory only for a new target or changed recorded source
  hashes, preserving earlier snapshots. Its command is:
    dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll upstream-inventory . ORIGINAL_START PINNED_TARGET NEW_INVENTORY_JSON
  Substitute verified full SHAs and a new destination. The start is inclusive.
  The generator initializes pending statuses; it does not preserve or certify
  manually reviewed completion, so keep behavior-level evidence separately.
- Update current documentation, including AGENTS.md's checkpoint if appropriate.
  Do not turn an inspected target into a completed-sync cursor prematurely.
- At a handoff, identify the next concrete pending capability and prerequisites,
  so the next session resumes implementation rather than repeating the audit.

Report the pinned range, concrete behavior changes, checks actually executed,
failures, retired functionality and remaining work. Claim full synchronization
only when the entire scoped backlog has a verified disposition. Local fixtures
do not establish live-provider behavior, remote CI success or release readiness.
```

## Usage notes

- The prompt intentionally reads the current inventory instead of hard-coding a
  moving "latest" target or a historical test count.
- To resume the same pinned target without network access, explicitly tell the
  agent to reuse that verified SHA and existing Git objects instead of fetching.
- To request a smaller subset, state it explicitly. Otherwise the full C# port,
  including previously missing capabilities, remains the requested objective.
