# Stage 5.2 Accessibility Evidence — Working 0.2

**Date:** 2026-07-28  
**Prototype:** Direction 2 P0 + edge-state wave  
**Status:** WORKING PASS

## Browser semantic evidence

| Surface | Evidence | Result |
|---|---|---|
| Endpoint form | labelled textbox, hint, alert/status and disabled continuation | PASS |
| Sign-in form | labelled login/password, explicit error codes and recoverable actions | PASS |
| Bootstrap | named progressbar with min/max/current value | PASS |
| Search | named dialog, textbox, filters, listbox/options and disabled redacted result | PASS |
| Inbox | named region, quick-capture field, listbox/options and inspector action | PASS |
| Conflict | named dialog, comparison table and explicit resolution buttons | PASS |
| Connectivity | textual status, diagnostics, retry/interruption and disabled write controls | PASS |
| Compact navigation | every icon-only navigation item retains an `aria-label` | PASS |
| Compact primary actions | Search, New Task and Profile retain accessible names | PASS |

## Keyboard evidence

- `Ctrl+K` opens Global Search.
- Arrow Up/Down changes the selected result.
- Enter attempts the selected result but cannot open a redacted target.
- Escape closes non-blocking overlays.
- Auth and bootstrap actions are operable with native button semantics.
- Reconnect, interruption, scope validation and diagnostics actions are keyboard-reachable.
- Disabled write controls expose disabled state while read-only content remains navigable.

## Scaling and responsive evidence

| Check | Evidence | Result |
|---|---|---|
| Active Windows/browser device scale | `devicePixelRatio = 1.5` | PASS at 150% |
| 1280 × 720 compact viewport | document/body width and height overflow = 0 | PASS |
| 800 × 660 constrained viewport | adaptive icon rail, stacked content and retained accessible names in browser accessibility snapshot | PASS |
| Long Russian edge messages | TLS, locked account, cursor expiry and reconnect banners wrap without clipping | PASS |
| Reduced motion | reconnect spinner disabled under `prefers-reduced-motion: reduce` | IMPLEMENTED |
| Actual Windows 200% scaling | requires controlled OS-level test session | PENDING |

## Issue found and corrected

The first 800 px adaptive snapshot showed icon-only navigation buttons without accessible names because visible labels were removed by responsive CSS. `aria-label` was added to all `NavItem` controls, the compact New Task action and the profile menu. The repeated browser accessibility snapshot then exposed the correct Russian names.

## Remaining formal evidence

- Windows UI Automation name/role/state capture;
- Narrator walkthrough and announcement behavior;
- contrast-tool measurements for all semantic colors;
- actual Windows 200% scaling and long-string screenshots;
- deterministic focus-return verification with the desktop runtime.

These remaining checks keep `S5-0214` and the accessibility gate open.

