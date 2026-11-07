# C# static website

The website's original page content, styling and JSON assets remain. A C# publisher
now copies and validates the static site without Node/npm or JavaScript:

```text
dotnet build Rea.slnx --configuration Release
dotnet exec scripts/bin/Release/net10.0/Rea.Validation.dll website . dotnet/artifacts/static-website
```

The migration retired fourteen JavaScript files, their executable inline blocks
and event handlers, and the obsolete Electron npm manifest. Recovery source
commits and hashes are recorded in csharp-migration.json. No folder was deleted.
Browser/Electron demos, clipboard actions, theme toggles and other script-driven
interactions are unavailable; pages carry a static-archive notice. These are
retirements of incompatible browser features, not claimed C# equivalents.

The publisher preserves public HTML/CSS/JSON/image assets and rejects JavaScript
inputs or overlapping output/source paths. It supports the repository's owned
markup, not arbitrary untrusted HTML sanitization. The complete upstream website
redesign and provider features remain pending in the upstream sync inventory.
