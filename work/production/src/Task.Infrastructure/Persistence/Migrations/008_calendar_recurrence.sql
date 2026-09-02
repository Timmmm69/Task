CREATE TABLE calendar.recurrence_series (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id),
    version bigint NOT NULL CHECK (version > 0),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    created_by uuid NOT NULL,
    definition jsonb NOT NULL CHECK (jsonb_typeof(definition) = 'object'),
    UNIQUE (organization_id, id)
);
CREATE INDEX ix_recurrence_series_org ON calendar.recurrence_series(organization_id, id);

CREATE TABLE calendar.recurrence_occurrences (
    organization_id uuid NOT NULL,
    series_id uuid NOT NULL,
    local_date date NOT NULL,
    task_id uuid NOT NULL,
    skipped boolean NOT NULL DEFAULT false,
    generated_task_version bigint NOT NULL DEFAULT 1 CHECK (generated_task_version > 0),
    template jsonb,
    is_exception boolean NOT NULL DEFAULT false,
    PRIMARY KEY (organization_id, series_id, local_date),
    UNIQUE (organization_id, task_id),
    FOREIGN KEY (organization_id, series_id) REFERENCES calendar.recurrence_series(organization_id, id),
    FOREIGN KEY (organization_id, task_id) REFERENCES work.tasks(organization_id, id)
);

-- Durable command responses are scoped by tenant, actor, resource and operation.
CREATE TABLE calendar.recurrence_commands (
    organization_id uuid NOT NULL REFERENCES core.organizations(id),
    actor_id uuid NOT NULL,
    resource_id uuid NOT NULL,
    operation varchar(80) NOT NULL,
    idempotency_key varchar(200) NOT NULL,
    request_hash varchar(64) NOT NULL,
    status integer NOT NULL,
    version bigint NOT NULL,
    response jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (organization_id, actor_id, resource_id, operation, idempotency_key)
);

ALTER TABLE governance.domain_events DROP CONSTRAINT ck_domain_event_aggregate;
ALTER TABLE governance.domain_events ADD CONSTRAINT ck_domain_event_aggregate
    CHECK (aggregate_type IN ('task', 'recurrence_series'));

INSERT INTO iam.permissions (code, description) VALUES
    ('recurrence.read', 'Read recurrence series and preview occurrences.'),
    ('recurrence.manage', 'Create and manage repeating task series.')
ON CONFLICT (code) DO NOTHING;
INSERT INTO iam.role_permissions(role_id, permission_code, effect)
SELECT role_id, 'recurrence.read', effect FROM iam.role_permissions WHERE permission_code = 'task.read'
ON CONFLICT DO NOTHING;
INSERT INTO iam.role_permissions(role_id, permission_code, effect)
SELECT role_id, 'recurrence.manage', effect FROM iam.role_permissions WHERE permission_code = 'task.manage'
ON CONFLICT DO NOTHING;
