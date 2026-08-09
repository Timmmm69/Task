
# Stage 2.3 Redocly Report

- Command: `docker run --rm -v "${root}:/work:ro" -w /work node:22-alpine sh -lc "npx --yes @redocly/cli@2.40.0 lint openapi/openapi.yaml --format=stylish"`.
- Redocly CLI: `2.40.0`.
- Configuration: built-in recommended configuration (no project override exists).
- Errors: **0**.
- Warnings: **0**.
- Exit code: **0**.
- Full log: `qa/reports/stage_2_3_runtime/redocly_lint_docker.log`.

The first Windows-local invocation completed validation successfully but the bundled Node process crashed during shutdown. The identical pinned command was repeated in the project Docker runtime and returned exit code 0; the container result is the release gate evidence.
