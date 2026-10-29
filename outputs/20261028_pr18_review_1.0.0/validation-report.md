# PR18 Windows review — 1.0.0

Reviewed original PR head 3d7890ca22b6394f685261175697455024cde596 integrated with main 0e170c22b2d61a99100901de58af09dfa002affb.

## Finding and fix
The editor requested a one-pixel rectangle at Loaded priority. With long contact lists its fields remained below the viewport; focus transfer from disabled action buttons could also undo the scroll. Request the full editor at ContextIdle after input/focus work. No focus is forcibly assigned. The new Windows regression covers add/edit channel, address and delete confirmation at 800×600 and 1100×1000. It fails on the original handler and passes after the fix.

## Validation
- Windows Release solution build and full whitespace gate: PASS.
- 3507 unique suite tests: 1922 Desktop, 671 ServiceHosts, 914 domain/API with real PostgreSQL 16, all passed with no skips. Final contact subset: 14/14.
- Dependency audit, project boundaries, independent security review and TLS contract: PASS.
- Original binding paths preserved. Public API/VM/database contracts unchanged.
- Live WPF synthetic contact window: mouse editing/autoscroll, channel cancellation, keyboard selection in both dropdowns, Tab traversal and both checkbox Space toggles: PASS.
- 800×600/1100×1000: editor fields and Save reachable, read-only buttons disabled, no binding errors. Raster exports at 100/150/200% are rendering checks, not physical monitor DPI changes.
- Forced contrast harness: the actual production XAML templates with test-only HighContrast conditions enabled render successfully; checked checkbox strokes/backgrounds resolve to system brushes. OS contrast switching and Narrator were not run.

## Reproduction
Run from repository root: dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --filter FullyQualifiedName~ContactRelationsScrollWindowsTests

Layout: dotnet run --project work/pr18_validation/Probe.csproj -c Release -- outputs/pr18-recheck

Forced contrast branches: append --contrast. This harness removes the code-behind event and manually scrolls for the contrast branch; the compiled production event is tested separately by the Windows regression.

## Evidence and limits
See validation.json, source-manifest.json, evidence/*.trx, layout/metrics.json and forced-contrast/metrics.json. Initial WIN1252 PostgreSQL failures are setup diagnostics; the final UTF8 PostgreSQL run passes. The initial full solution run used the prior test binary for Desktop; desktop-final.trx is the authoritative final Desktop run. Native UI used a synthetic local client; no customer/server deployment or clean-Windows/mixed-monitor acceptance is claimed. CI must be green for the published PR head before merge; live GitHub results are verified separately at publication.
