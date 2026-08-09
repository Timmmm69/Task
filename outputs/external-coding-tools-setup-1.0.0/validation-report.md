# Validation report — external coding tools setup 1.0.0

Date: 2026-07-26

| Check | Result |
| --- | --- |
| Windows-native environment and Codex home identified | Pass |
| Node.js, npm/npx and uv/uvx available | Pass |
| Serena and Semble installed by their official installers | Pass |
| `config.toml` syntax | Pass |
| `hooks.json` syntax | Pass |
| `External coding tools` section in global `AGENTS.md` | Pass |
| One MCP entry each for Context7, Serena and Semble | Pass |
| `codex mcp list` includes Context7, Serena and Semble as enabled | Pass |
| Serena project activation, language server and symbol health check | Pass |
| Semble semantic search | Pass |
| Context7 documentation query (`React` / `useState`) | Pass |

Context7, Serena and Semble are registered globally and will be loaded by newly started Codex sessions. Restart the Codex desktop application before checking their live connection in `/mcp`.

Backup location: `C:\Users\novik\.codex\backups\external-coding-tools-20260726-150349`.
