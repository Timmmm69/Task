# Native Windows UIA and Narrator Protocol

## Preconditions

- Use the compiled Windows desktop client, not the browser prototype.
- Record application version, executable SHA-256, Windows version/build, screen-reader version, locale, user role, server fixture and timestamp.
- Use production-like authorized data with no customer secrets.

## Procedure

For each checkpoint in `windows/Windows_Accessibility_Checkpoints.csv`: capture Inspect/UIA properties, Narrator output notes, keyboard path, focus order/return and a screenshot or screen recording reference. Mark PASS only when the expected name, role, state/value and user outcome are all demonstrated.

## Stop conditions

Stop and file a Critical/High finding for a focus trap, inaccessible required action, undisclosed destructive consequence, permission disclosure, accepted write in offline/read-only mode, or loss of user input. Do not sign Gate 5.6 while any Critical/High remains open.
