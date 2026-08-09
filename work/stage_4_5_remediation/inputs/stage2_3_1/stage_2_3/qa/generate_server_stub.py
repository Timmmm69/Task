from __future__ import annotations

from pathlib import Path

import yaml


ROOT = Path(__file__).resolve().parents[1]
OPENAPI = ROOT / "openapi" / "openapi.yaml"
OUTPUT = ROOT / "qa" / "generated" / "server-contract" / "handlers.ts"
HTTP_METHODS = {"get", "post", "put", "patch", "delete"}

document = yaml.safe_load(OPENAPI.read_text(encoding="utf-8"))
operation_ids = sorted(
    operation["operationId"]
    for path_item in document["paths"].values()
    for method, operation in path_item.items()
    if method.lower() in HTTP_METHODS
)
if len(operation_ids) != 244 or len(operation_ids) != len(set(operation_ids)):
    raise RuntimeError(
        f"Expected 244 unique operation IDs for server stub, got {len(operation_ids)}"
    )

quoted_operation_ids = ",\n".join(f'  "{operation_id}"' for operation_id in operation_ids)
source = f"""import type {{ operations }} from "./schema";

export const operationIds = [
{quoted_operation_ids},
] as const satisfies readonly (keyof operations)[];

export type OperationId = (typeof operationIds)[number];

export type OperationContext<Operation extends OperationId> = {{
  operationId: Operation;
  parameters: operations[Operation] extends {{ parameters: infer Parameters }}
    ? Parameters
    : Record<string, never>;
  requestBody: operations[Operation] extends {{
    requestBody: {{ content: {{ "application/json": infer Body }} }};
  }}
    ? Body
    : undefined;
  principal: {{
    organizationId: string;
    userAccountId: string;
    permissionCodes: readonly string[];
  }} | null;
  idempotencyKey: string | null;
  ifMatch: string | null;
  correlationId: string;
}};

export type ServerResult = {{
  status: number;
  headers: Readonly<Record<string, string>>;
  body?: unknown;
}};

export type OrganizerServerHandlers = {{
  [Operation in OperationId]: (
    context: OperationContext<Operation>,
  ) => Promise<ServerResult>;
}};

export const createNotImplementedHandlers = (): OrganizerServerHandlers =>
  Object.fromEntries(
    operationIds.map((operationId) => [
      operationId,
      async (): Promise<ServerResult> => ({{
        status: 501,
        headers: {{ "content-type": "application/problem+json" }},
        body: {{
          type: "about:blank",
          title: "Not implemented",
          status: 501,
          code: "NOT_IMPLEMENTED",
          operationId,
        }},
      }}),
    ]),
  ) as unknown as OrganizerServerHandlers;
"""

OUTPUT.parent.mkdir(parents=True, exist_ok=True)
OUTPUT.write_text(source, encoding="utf-8", newline="\n")
print(f"SERVER_STUB_GENERATED operations={len(operation_ids)}")
