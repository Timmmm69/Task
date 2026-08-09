-- Organizer Stage 2.1 production-readiness corrections.
-- PostgreSQL 16+. This migration is applied once by the migration ledger.
BEGIN;

-- Lifecycle ownership is explicit: roles, recurrence series, reminders and comments
-- have their own state machines; checklists remain aggregate-owned task children.
ALTER TABLE iam.roles
    ADD COLUMN status varchar(16) NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','inactive')),
    ADD COLUMN deactivated_at timestamptz,
    ADD COLUMN deactivated_by uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    ADD CONSTRAINT ck_roles_deactivation
        CHECK ((status = 'inactive' AND deactivated_at IS NOT NULL) OR status = 'active');

ALTER TABLE projects.project_roles
    ADD COLUMN status varchar(16) NOT NULL DEFAULT 'active'
        CHECK (status IN ('active','inactive')),
    ADD COLUMN deactivated_at timestamptz,
    ADD CONSTRAINT ck_project_roles_deactivation
        CHECK ((status = 'inactive' AND deactivated_at IS NOT NULL) OR status = 'active');

ALTER TABLE collab.comments
    ADD COLUMN parent_comment_id uuid REFERENCES collab.comments(id) ON DELETE RESTRICT,
    ADD CONSTRAINT ck_comments_parent_not_self
        CHECK (parent_comment_id IS NULL OR parent_comment_id <> id);
CREATE INDEX ix_comments_parent ON collab.comments (parent_comment_id, created_at, id)
    WHERE parent_comment_id IS NOT NULL;

CREATE TABLE calendar.event_contact_attendees (
    event_id uuid NOT NULL REFERENCES calendar.calendar_events(id) ON DELETE CASCADE,
    contact_id uuid NOT NULL REFERENCES crm.contacts(id) ON DELETE RESTRICT,
    attendee_role varchar(16) NOT NULL DEFAULT 'required'
        CHECK (attendee_role IN ('required','optional','observer')),
    response_status varchar(16) NOT NULL DEFAULT 'pending'
        CHECK (response_status IN ('pending','accepted','declined','tentative')),
    response_channel varchar(20),
    responded_at timestamptz,
    PRIMARY KEY (event_id, contact_id)
);
CREATE INDEX ix_event_contact_attendees_contact
    ON calendar.event_contact_attendees (contact_id, event_id, response_status);
CREATE UNIQUE INDEX uq_contact_company_one_primary
    ON crm.contact_company_roles (contact_id)
    WHERE is_primary AND valid_to IS NULL;

-- A recurrence series owns a complete immutable-at-generation task template.
CREATE TABLE work.recurrence_task_templates (
    series_id uuid PRIMARY KEY REFERENCES work.recurrence_series(id) ON DELETE CASCADE,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    project_id uuid REFERENCES projects.projects(id) ON DELETE RESTRICT,
    title text NOT NULL CHECK (length(btrim(title)) BETWEEN 1 AND 500),
    description text,
    author_user_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    creator_user_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    requester_user_id uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    primary_counterparty_object_id uuid REFERENCES core.objects(id) ON DELETE SET NULL,
    priority varchar(16) NOT NULL DEFAULT 'normal'
        CHECK (priority IN ('low','normal','high','critical')),
    planned_duration_minutes integer CHECK (planned_duration_minutes BETWEEN 1 AND 10080),
    deadline_offset_minutes integer,
    template_version bigint NOT NULL DEFAULT 1 CHECK (template_version > 0),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_recurrence_templates_org_series UNIQUE (organization_id, series_id)
);
CREATE INDEX ix_recurrence_templates_project
    ON work.recurrence_task_templates (organization_id, project_id)
    WHERE project_id IS NOT NULL;

CREATE TABLE work.recurrence_template_assignees (
    series_id uuid NOT NULL REFERENCES work.recurrence_task_templates(series_id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    is_primary boolean NOT NULL DEFAULT false,
    PRIMARY KEY (series_id, user_account_id)
);
CREATE UNIQUE INDEX uq_recurrence_template_primary_assignee
    ON work.recurrence_template_assignees (series_id) WHERE is_primary;

CREATE TABLE work.recurrence_template_watchers (
    series_id uuid NOT NULL REFERENCES work.recurrence_task_templates(series_id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    PRIMARY KEY (series_id, user_account_id)
);

CREATE TABLE work.recurrence_template_checklists (
    id uuid PRIMARY KEY,
    series_id uuid NOT NULL REFERENCES work.recurrence_task_templates(series_id) ON DELETE CASCADE,
    title text NOT NULL CHECK (length(btrim(title)) BETWEEN 1 AND 300),
    sort_order numeric(20,10) NOT NULL DEFAULT 0
);
CREATE INDEX ix_recurrence_template_checklists
    ON work.recurrence_template_checklists (series_id, sort_order, id);

CREATE TABLE work.recurrence_template_checklist_items (
    id uuid PRIMARY KEY,
    checklist_id uuid NOT NULL REFERENCES work.recurrence_template_checklists(id) ON DELETE CASCADE,
    text_value text NOT NULL CHECK (length(btrim(text_value)) BETWEEN 1 AND 1000),
    sort_order numeric(20,10) NOT NULL DEFAULT 0
);
CREATE INDEX ix_recurrence_template_checklist_items
    ON work.recurrence_template_checklist_items (checklist_id, sort_order, id);

CREATE TABLE work.recurrence_template_reminder_rules (
    id uuid PRIMARY KEY,
    series_id uuid NOT NULL REFERENCES work.recurrence_task_templates(series_id) ON DELETE CASCADE,
    recipient_user_id uuid REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    trigger_type varchar(24) NOT NULL
        CHECK (trigger_type IN ('before_start','before_deadline','at_start','at_deadline')),
    offset_minutes integer,
    CHECK (
        (trigger_type IN ('at_start','at_deadline') AND offset_minutes IS NULL)
        OR (trigger_type IN ('before_start','before_deadline') AND offset_minutes >= 0)
    )
);

CREATE OR REPLACE FUNCTION work.require_recurrence_template()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM work.recurrence_task_templates
        WHERE series_id = NEW.id AND organization_id = NEW.organization_id
    ) THEN
        RAISE EXCEPTION 'RECURRENCE_TEMPLATE_REQUIRED' USING ERRCODE = '23514';
    END IF;
    RETURN NULL;
END;
$$;
CREATE CONSTRAINT TRIGGER trg_recurrence_template_required
AFTER INSERT OR UPDATE OF organization_id ON work.recurrence_series
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION work.require_recurrence_template();

-- Reminder state is explicit and all retryable work has a bounded lease.
ALTER TABLE calendar.reminders
    ADD COLUMN delivered_at timestamptz,
    ADD COLUMN snoozed_until timestamptz,
    ADD COLUMN cancelled_at timestamptz,
    ADD COLUMN expired_at timestamptz,
    ADD CONSTRAINT ck_reminders_state_dates CHECK (
        (status <> 'delivered' OR delivered_at IS NOT NULL)
        AND (status <> 'snoozed' OR snoozed_until IS NOT NULL)
        AND (status <> 'cancelled' OR cancelled_at IS NOT NULL)
        AND (status <> 'expired' OR expired_at IS NOT NULL)
    );

ALTER TABLE calendar.reminder_occurrences
    ALTER COLUMN status SET DEFAULT 'created',
    DROP CONSTRAINT reminder_occurrences_status_check,
    ADD CONSTRAINT reminder_occurrences_status_check
        CHECK (status IN ('created','claimed','delivered','failed','dead_letter','cancelled')),
    ADD COLUMN lock_token uuid,
    ADD COLUMN lease_expires_at timestamptz,
    ADD COLUMN heartbeat_at timestamptz,
    ADD COLUMN next_attempt_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    ADD CONSTRAINT ck_reminder_occurrence_lease CHECK (
        (status = 'claimed' AND lock_token IS NOT NULL AND lease_expires_at IS NOT NULL)
        OR status <> 'claimed'
    );
DROP INDEX calendar.ix_reminder_occurrences_claim;
CREATE INDEX ix_reminder_occurrences_claim
    ON calendar.reminder_occurrences (status, next_attempt_at, due_at, id)
    WHERE status IN ('created','failed') OR (status = 'claimed' AND lease_expires_at IS NOT NULL);

-- Durable command idempotency. Request hashes are SHA-256 bytes; responses are replayed verbatim.
CREATE TABLE iam.idempotency_records (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    operation_id varchar(160) NOT NULL,
    idempotency_key varchar(200) NOT NULL,
    request_hash bytea NOT NULL CHECK (octet_length(request_hash) = 32),
    state varchar(16) NOT NULL DEFAULT 'in_progress'
        CHECK (state IN ('in_progress','completed','failed')),
    response_status smallint CHECK (response_status BETWEEN 100 AND 599),
    response_headers jsonb,
    response_body jsonb,
    resource_id uuid,
    locked_by uuid,
    lock_expires_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    completed_at timestamptz,
    expires_at timestamptz NOT NULL,
    CONSTRAINT uq_idempotency_scope
        UNIQUE (organization_id, user_account_id, operation_id, idempotency_key),
    CONSTRAINT ck_idempotency_expiry CHECK (expires_at > created_at),
    CONSTRAINT ck_idempotency_completed CHECK (
        (state = 'completed' AND response_status IS NOT NULL AND completed_at IS NOT NULL)
        OR state <> 'completed'
    )
);
CREATE INDEX ix_idempotency_expiry ON iam.idempotency_records (expires_at);
CREATE INDEX ix_idempotency_in_progress
    ON iam.idempotency_records (lock_expires_at)
    WHERE state = 'in_progress';

CREATE OR REPLACE FUNCTION iam.acquire_idempotency_record(
    new_record_id uuid,
    target_organization_id uuid,
    target_user_account_id uuid,
    target_operation_id varchar,
    target_idempotency_key varchar,
    target_request_hash bytea,
    request_owner uuid,
    lease_duration interval,
    retention_duration interval
)
RETURNS TABLE (
    disposition text,
    stored_response_status smallint,
    stored_response_headers jsonb,
    stored_response_body jsonb,
    stored_resource_id uuid
)
LANGUAGE plpgsql
AS $$
DECLARE
    existing iam.idempotency_records%ROWTYPE;
BEGIN
    INSERT INTO iam.idempotency_records (
        id, organization_id, user_account_id, operation_id, idempotency_key,
        request_hash, locked_by, lock_expires_at, expires_at
    )
    VALUES (
        new_record_id, target_organization_id, target_user_account_id,
        target_operation_id, target_idempotency_key, target_request_hash,
        request_owner, clock_timestamp() + lease_duration,
        clock_timestamp() + retention_duration
    )
    ON CONFLICT (organization_id, user_account_id, operation_id, idempotency_key)
    DO NOTHING;

    SELECT record_row.*
      INTO existing
      FROM iam.idempotency_records AS record_row
     WHERE record_row.organization_id = target_organization_id
       AND record_row.user_account_id = target_user_account_id
       AND record_row.operation_id = target_operation_id
       AND record_row.idempotency_key = target_idempotency_key
     FOR UPDATE;

    IF existing.request_hash <> target_request_hash THEN
        RAISE EXCEPTION 'IDEMPOTENCY_KEY_REUSED' USING ERRCODE = '23514';
    END IF;
    IF existing.state = 'completed' THEN
        RETURN QUERY SELECT
            'replay'::text,
            existing.response_status,
            existing.response_headers,
            existing.response_body,
            existing.resource_id;
        RETURN;
    END IF;
    IF existing.state = 'in_progress'
       AND existing.locked_by IS DISTINCT FROM request_owner
       AND existing.lock_expires_at >= clock_timestamp() THEN
        RETURN QUERY SELECT
            'in_progress'::text,
            NULL::smallint,
            NULL::jsonb,
            NULL::jsonb,
            NULL::uuid;
        RETURN;
    END IF;

    UPDATE iam.idempotency_records
       SET state = 'in_progress',
           locked_by = request_owner,
           lock_expires_at = clock_timestamp() + lease_duration,
           expires_at = greatest(expires_at, clock_timestamp() + retention_duration)
     WHERE id = existing.id;
    RETURN QUERY SELECT
        'execute'::text,
        NULL::smallint,
        NULL::jsonb,
        NULL::jsonb,
        NULL::uuid;
END;
$$;

DROP FUNCTION IF EXISTS iam.complete_idempotency_record(uuid, uuid, bytea, smallint, jsonb, jsonb, uuid);
CREATE OR REPLACE FUNCTION iam.complete_idempotency_record(
    target_record_id uuid,
    request_owner uuid,
    target_request_hash bytea,
    result_status integer,
    result_headers jsonb,
    result_body jsonb,
    result_resource_id uuid
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    IF result_status < 100 OR result_status > 599 THEN
        RAISE EXCEPTION 'response status % is outside HTTP status range', result_status
            USING ERRCODE = '22023';
    END IF;

    UPDATE iam.idempotency_records
       SET state = 'completed',
           response_status = result_status,
           response_headers = coalesce(result_headers, '{}'::jsonb),
           response_body = result_body,
           resource_id = result_resource_id,
           completed_at = clock_timestamp(),
           locked_by = NULL,
           lock_expires_at = NULL
     WHERE id = target_record_id
       AND state = 'in_progress'
       AND locked_by = request_owner
       AND request_hash = target_request_hash;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'IDEMPOTENCY_LEASE_OR_HASH_MISMATCH' USING ERRCODE = '40001';
    END IF;
END;
$$;

-- Per-device file availability replaces the unsafe global availability flag.
ALTER TABLE files.file_locations
    ADD COLUMN owner_user_id uuid REFERENCES iam.user_accounts(id) ON DELETE CASCADE;
UPDATE files.file_locations SET owner_user_id = created_by WHERE owner_user_id IS NULL;
ALTER TABLE files.file_locations ALTER COLUMN owner_user_id SET NOT NULL;
ALTER TABLE files.file_locations
    DROP COLUMN availability_status,
    DROP COLUMN last_checked_at,
    DROP COLUMN last_checked_by_device_id;

CREATE TABLE files.file_location_device_states (
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    file_location_id uuid NOT NULL REFERENCES files.file_locations(id) ON DELETE CASCADE,
    device_id uuid NOT NULL REFERENCES iam.devices(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    availability_status varchar(24) NOT NULL DEFAULT 'unknown'
        CHECK (availability_status IN (
            'unknown','available','not_found','access_denied',
            'resource_unavailable','invalid_path','timeout'
        )),
    last_checked_at timestamptz,
    latency_ms integer CHECK (latency_ms IS NULL OR latency_ms >= 0),
    last_check_id uuid REFERENCES files.file_location_checks(id) ON DELETE SET NULL,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    PRIMARY KEY (file_location_id, device_id)
);
CREATE INDEX ix_file_location_device_states_device
    ON files.file_location_device_states (organization_id, device_id, availability_status, last_checked_at DESC);

CREATE OR REPLACE FUNCTION files.enforce_file_location_security()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    resource_org uuid;
    resource_root text;
    resource_status text;
BEGIN
    IF NEW.normalized_path ~ '(^|[\\/])\.\.([\\/]|$)' THEN
        RAISE EXCEPTION 'FILE_PATH_TRAVERSAL_FORBIDDEN' USING ERRCODE = '23514';
    END IF;

    IF NEW.location_type = 'local_path' THEN
        IF NEW.device_id IS NULL OR NEW.owner_user_id IS NULL OR NEW.network_resource_id IS NOT NULL THEN
            RAISE EXCEPTION 'LOCAL_PATH_REQUIRES_OWNER_DEVICE' USING ERRCODE = '23514';
        END IF;
        RETURN NEW;
    END IF;

    IF NEW.network_resource_id IS NULL THEN
        RAISE EXCEPTION 'NETWORK_PATH_REQUIRES_APPROVED_RESOURCE' USING ERRCODE = '23514';
    END IF;

    SELECT organization_id, normalized_root_path, status
      INTO resource_org, resource_root, resource_status
      FROM files.network_resources
     WHERE id = NEW.network_resource_id
     FOR KEY SHARE;

    IF resource_org IS DISTINCT FROM NEW.organization_id OR resource_status = 'retired' THEN
        RAISE EXCEPTION 'NETWORK_RESOURCE_NOT_APPROVED' USING ERRCODE = '23514';
    END IF;
    IF lower(NEW.normalized_path) <> lower(resource_root)
       AND left(lower(NEW.normalized_path), length(resource_root) + 1) <> lower(resource_root) || '\' THEN
        RAISE EXCEPTION 'NETWORK_PATH_OUTSIDE_APPROVED_ROOT' USING ERRCODE = '23514';
    END IF;
    IF NEW.location_type = 'unc_path' AND NEW.device_id IS NOT NULL THEN
        RAISE EXCEPTION 'UNC_PATH_MUST_NOT_BE_DEVICE_BOUND' USING ERRCODE = '23514';
    END IF;
    IF NEW.location_type = 'mapped_drive' AND NEW.device_id IS NULL THEN
        RAISE EXCEPTION 'MAPPED_DRIVE_REQUIRES_DEVICE' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER trg_file_location_security
BEFORE INSERT OR UPDATE OF organization_id, location_type, normalized_path, device_id,
    owner_user_id, network_resource_id
ON files.file_locations
FOR EACH ROW EXECUTE FUNCTION files.enforce_file_location_security();

CREATE VIEW files.file_locations_redacted AS
SELECT
    id,
    organization_id,
    catalog_item_id,
    location_type,
    CASE
        WHEN location_type = 'local_path' THEN '[local path hidden]'
        WHEN location_type = 'mapped_drive' THEN '[mapped path hidden]'
        ELSE regexp_replace(raw_path, '^((\\\\[^\\]+\\)[^\\]+).*$', '\1…')
    END AS display_path,
    device_id,
    network_resource_id,
    priority,
    is_enabled,
    is_primary,
    version,
    owner_user_id,
    created_at,
    updated_at
FROM files.file_locations;
COMMENT ON VIEW files.file_locations_redacted IS
    'Default API projection. Full raw_path requires ownership of the bound device or FileLocation.ReadSensitivePath.';

-- Change feed is produced only by an idempotent domain-event projector.
ALTER TABLE sync.change_feed
    ADD COLUMN source_event_id uuid REFERENCES governance.domain_events(id) ON DELETE RESTRICT;
ALTER TABLE sync.change_feed ALTER COLUMN source_event_id SET NOT NULL;
CREATE UNIQUE INDEX uq_change_feed_source_object
    ON sync.change_feed (organization_id, source_event_id, object_id, operation);

CREATE OR REPLACE FUNCTION sync.project_domain_event_change(
    source_event uuid,
    changed_object_id uuid,
    changed_object_type varchar,
    change_operation varchar,
    changed_object_version bigint,
    changed_permission_scope_version bigint,
    changed_field_names text[],
    hint jsonb
)
RETURNS bigint
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, governance, sync
AS $$
DECLARE
    event_row governance.domain_events%ROWTYPE;
    projected_sequence bigint;
BEGIN
    SELECT * INTO event_row
    FROM governance.domain_events
    WHERE id = source_event;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'SOURCE_EVENT_NOT_FOUND' USING ERRCODE = '23503';
    END IF;

    INSERT INTO sync.change_feed (
        organization_id, object_id, object_type, operation, object_version,
        permission_scope_version, changed_fields, payload_hint, correlation_id, source_event_id
    )
    VALUES (
        event_row.organization_id, changed_object_id, changed_object_type, change_operation,
        changed_object_version, changed_permission_scope_version, coalesce(changed_field_names, '{}'),
        coalesce(hint, '{}'::jsonb), event_row.correlation_id, source_event
    )
    ON CONFLICT (organization_id, source_event_id, object_id, operation)
    DO UPDATE SET source_event_id = EXCLUDED.source_event_id
    RETURNING sequence INTO projected_sequence;
    RETURN projected_sequence;
END;
$$;

CREATE TABLE sync.snapshot_sessions (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    device_id uuid NOT NULL REFERENCES iam.devices(id) ON DELETE CASCADE,
    cut_sequence bigint NOT NULL CHECK (cut_sequence >= 0),
    scope_version bigint NOT NULL CHECK (scope_version > 0),
    status varchar(16) NOT NULL DEFAULT 'building'
        CHECK (status IN ('building','ready','completed','expired','failed')),
    dataset_manifest jsonb NOT NULL DEFAULT '{}'::jsonb,
    page_size smallint NOT NULL CHECK (page_size BETWEEN 1 AND 500),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    ready_at timestamptz,
    completed_at timestamptz,
    expires_at timestamptz NOT NULL,
    CONSTRAINT ck_snapshot_expiry CHECK (expires_at > created_at),
    CONSTRAINT ck_snapshot_ready CHECK ((status <> 'ready') OR ready_at IS NOT NULL)
);
CREATE INDEX ix_snapshot_sessions_client
    ON sync.snapshot_sessions (organization_id, user_account_id, device_id, created_at DESC);
CREATE INDEX ix_snapshot_sessions_expiry ON sync.snapshot_sessions (expires_at);

CREATE TABLE sync.snapshot_session_items (
    snapshot_session_id uuid NOT NULL REFERENCES sync.snapshot_sessions(id) ON DELETE CASCADE,
    dataset varchar(40) NOT NULL,
    ordinal bigint NOT NULL CHECK (ordinal > 0),
    object_id uuid NOT NULL,
    object_type varchar(40) NOT NULL,
    object_version bigint NOT NULL CHECK (object_version > 0),
    payload jsonb NOT NULL,
    PRIMARY KEY (snapshot_session_id, dataset, ordinal),
    CONSTRAINT uq_snapshot_item UNIQUE (snapshot_session_id, dataset, object_id)
);
CREATE INDEX ix_snapshot_items_object
    ON sync.snapshot_session_items (snapshot_session_id, object_id);

-- Lease/token protocol for outbox and background jobs.
ALTER TABLE governance.outbox_messages
    ADD COLUMN lock_token uuid,
    ADD COLUMN lease_expires_at timestamptz,
    ADD COLUMN heartbeat_at timestamptz,
    ADD COLUMN next_attempt_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    ADD CONSTRAINT ck_outbox_lease CHECK (
        (status = 'processing' AND locked_by IS NOT NULL AND lock_token IS NOT NULL AND lease_expires_at IS NOT NULL)
        OR status <> 'processing'
    );
DROP INDEX governance.ix_outbox_claim;
CREATE INDEX ix_outbox_claim
    ON governance.outbox_messages (status, next_attempt_at, available_at, created_at)
    WHERE status IN ('pending','failed') OR (status = 'processing' AND lease_expires_at IS NOT NULL);

ALTER TABLE ops.background_job_runs
    ADD COLUMN lock_token uuid,
    ADD COLUMN lease_expires_at timestamptz,
    ADD COLUMN heartbeat_at timestamptz,
    ADD COLUMN next_attempt_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    ADD CONSTRAINT ck_background_job_lease CHECK (
        (status = 'running' AND worker_id IS NOT NULL AND lock_token IS NOT NULL AND lease_expires_at IS NOT NULL)
        OR status <> 'running'
    );
DROP INDEX ops.ix_background_job_runs_claim;
CREATE INDEX ix_background_job_runs_claim
    ON ops.background_job_runs (status, next_attempt_at, scheduled_at)
    WHERE status IN ('queued','failed') OR (status = 'running' AND lease_expires_at IS NOT NULL);

CREATE OR REPLACE FUNCTION ops.claim_outbox(
    worker_name text,
    worker_token uuid,
    lease_duration interval,
    batch_size integer
)
RETURNS SETOF governance.outbox_messages
LANGUAGE sql
AS $$
    WITH candidates AS (
        SELECT id
        FROM governance.outbox_messages
        WHERE (
            (status IN ('pending','failed') AND next_attempt_at <= clock_timestamp() AND available_at <= clock_timestamp())
            OR (status = 'processing' AND lease_expires_at < clock_timestamp())
        )
        ORDER BY available_at, created_at
        FOR UPDATE SKIP LOCKED
        LIMIT least(greatest(batch_size, 1), 500)
    )
    UPDATE governance.outbox_messages AS message
       SET status = 'processing',
           locked_by = worker_name,
           lock_token = worker_token,
           lease_expires_at = clock_timestamp() + lease_duration,
           heartbeat_at = clock_timestamp(),
           attempt_count = attempt_count + 1
      FROM candidates
     WHERE message.id = candidates.id
    RETURNING message.*;
$$;

CREATE OR REPLACE FUNCTION ops.complete_outbox(message_id uuid, worker_token uuid)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE governance.outbox_messages
       SET status = 'published',
           published_at = clock_timestamp(),
           locked_by = NULL,
           lock_token = NULL,
           lease_expires_at = NULL
     WHERE id = message_id
       AND status = 'processing'
       AND lock_token = worker_token;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'OUTBOX_LEASE_LOST' USING ERRCODE = '40001';
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION ops.fail_outbox(
    message_id uuid,
    worker_token uuid,
    failure_code varchar,
    failure_detail text
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE governance.outbox_messages
       SET status = CASE WHEN attempt_count >= 10 THEN 'dead_letter' ELSE 'failed' END,
           next_attempt_at = clock_timestamp() + make_interval(secs => least(3600, 5 * (2 ^ least(attempt_count, 9))::integer)),
           last_error_code = failure_code,
           last_error_detail = left(failure_detail, 4000),
           locked_by = NULL,
           lock_token = NULL,
           lease_expires_at = NULL
     WHERE id = message_id
       AND status = 'processing'
       AND lock_token = worker_token;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'OUTBOX_LEASE_LOST' USING ERRCODE = '40001';
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION ops.heartbeat_outbox(
    message_id uuid,
    worker_token uuid,
    lease_duration interval
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE governance.outbox_messages
       SET heartbeat_at = clock_timestamp(),
           lease_expires_at = clock_timestamp() + lease_duration
     WHERE id = message_id
       AND status = 'processing'
       AND lock_token = worker_token
       AND lease_expires_at >= clock_timestamp();
    IF NOT FOUND THEN
        RAISE EXCEPTION 'OUTBOX_LEASE_LOST' USING ERRCODE = '40001';
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION ops.claim_background_jobs(
    worker_name text,
    worker_token uuid,
    lease_duration interval,
    batch_size integer
)
RETURNS SETOF ops.background_job_runs
LANGUAGE sql
AS $$
    WITH candidates AS (
        SELECT run.id
        FROM ops.background_job_runs AS run
        JOIN ops.background_jobs AS job ON job.id = run.job_id
        WHERE (
            (run.status IN ('queued','failed')
             AND run.next_attempt_at <= clock_timestamp()
             AND run.scheduled_at <= clock_timestamp()
             AND run.attempt < job.max_attempts)
            OR (run.status = 'running' AND run.lease_expires_at < clock_timestamp())
        )
        ORDER BY run.scheduled_at, run.id
        FOR UPDATE OF run SKIP LOCKED
        LIMIT least(greatest(batch_size, 1), 500)
    )
    UPDATE ops.background_job_runs AS run
       SET status = 'running',
           worker_id = worker_name,
           lock_token = worker_token,
           lease_expires_at = clock_timestamp() + lease_duration,
           heartbeat_at = clock_timestamp(),
           started_at = coalesce(started_at, clock_timestamp()),
           attempt = attempt + 1
      FROM candidates
     WHERE run.id = candidates.id
    RETURNING run.*;
$$;

CREATE OR REPLACE FUNCTION ops.complete_background_job(
    run_id uuid,
    worker_token uuid,
    result jsonb
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE ops.background_job_runs
       SET status = 'succeeded',
           result_payload = coalesce(result, '{}'::jsonb),
           finished_at = clock_timestamp(),
           worker_id = NULL,
           lock_token = NULL,
           lease_expires_at = NULL
     WHERE id = run_id
       AND status = 'running'
       AND lock_token = worker_token;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'BACKGROUND_JOB_LEASE_LOST' USING ERRCODE = '40001';
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION ops.fail_background_job(
    run_id uuid,
    worker_token uuid,
    failure_code varchar,
    failure_detail text
)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    allowed_attempts integer;
    current_attempt integer;
BEGIN
    SELECT job.max_attempts, run.attempt
      INTO allowed_attempts, current_attempt
      FROM ops.background_job_runs AS run
      JOIN ops.background_jobs AS job ON job.id = run.job_id
     WHERE run.id = run_id
       AND run.status = 'running'
       AND run.lock_token = worker_token
     FOR UPDATE OF run;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'BACKGROUND_JOB_LEASE_LOST' USING ERRCODE = '40001';
    END IF;

    UPDATE ops.background_job_runs
       SET status = CASE WHEN current_attempt >= allowed_attempts THEN 'dead_letter' ELSE 'failed' END,
           next_attempt_at = clock_timestamp() + make_interval(secs => least(3600, 5 * (2 ^ least(current_attempt, 9))::integer)),
           error_code = failure_code,
           error_detail = left(failure_detail, 4000),
           finished_at = CASE WHEN current_attempt >= allowed_attempts THEN clock_timestamp() ELSE NULL END,
           worker_id = NULL,
           lock_token = NULL,
           lease_expires_at = NULL
     WHERE id = run_id;
END;
$$;

CREATE OR REPLACE FUNCTION ops.claim_reminder_occurrences(
    worker_name text,
    worker_token uuid,
    lease_duration interval,
    batch_size integer
)
RETURNS SETOF calendar.reminder_occurrences
LANGUAGE sql
AS $$
    WITH candidates AS (
        SELECT id
        FROM calendar.reminder_occurrences
        WHERE (
            (status IN ('created','failed') AND next_attempt_at <= clock_timestamp() AND due_at <= clock_timestamp())
            OR (status = 'claimed' AND lease_expires_at < clock_timestamp())
        )
          AND attempt_count < 10
        ORDER BY due_at, id
        FOR UPDATE SKIP LOCKED
        LIMIT least(greatest(batch_size, 1), 500)
    )
    UPDATE calendar.reminder_occurrences AS occurrence
       SET status = 'claimed',
           claimed_by_worker = worker_name,
           claimed_at = clock_timestamp(),
           lock_token = worker_token,
           lease_expires_at = clock_timestamp() + lease_duration,
           heartbeat_at = clock_timestamp(),
           attempt_count = attempt_count + 1
      FROM candidates
     WHERE occurrence.id = candidates.id
    RETURNING occurrence.*;
$$;

CREATE OR REPLACE FUNCTION ops.complete_reminder_occurrence(
    occurrence_id uuid,
    worker_token uuid
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE calendar.reminder_occurrences
       SET status = 'delivered',
           delivered_at = clock_timestamp(),
           claimed_by_worker = NULL,
           lock_token = NULL,
           lease_expires_at = NULL
     WHERE id = occurrence_id
       AND status = 'claimed'
       AND lock_token = worker_token;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'REMINDER_LEASE_LOST' USING ERRCODE = '40001';
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION ops.fail_reminder_occurrence(
    occurrence_id uuid,
    worker_token uuid,
    failure_code varchar
)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE calendar.reminder_occurrences
       SET status = CASE WHEN attempt_count >= 10 THEN 'dead_letter' ELSE 'failed' END,
           next_attempt_at = clock_timestamp() + make_interval(secs => least(3600, 5 * (2 ^ least(attempt_count, 9))::integer)),
           last_error_code = failure_code,
           claimed_by_worker = NULL,
           lock_token = NULL,
           lease_expires_at = NULL
     WHERE id = occurrence_id
       AND status = 'claimed'
       AND lock_token = worker_token;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'REMINDER_LEASE_LOST' USING ERRCODE = '40001';
    END IF;
END;
$$;

-- Audit/history uniqueness, tombstones and append-only runtime permissions.
CREATE TABLE governance.object_history_version_keys (
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    object_id uuid NOT NULL,
    object_version bigint NOT NULL CHECK (object_version > 0),
    history_id uuid NOT NULL,
    changed_at timestamptz NOT NULL,
    PRIMARY KEY (organization_id, object_id, object_version),
    UNIQUE (history_id, changed_at)
);

CREATE OR REPLACE FUNCTION governance.reserve_object_history_version()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO governance.object_history_version_keys (
        organization_id, object_id, object_version, history_id, changed_at
    )
    VALUES (
        NEW.organization_id, NEW.object_id, NEW.object_version, NEW.id, NEW.changed_at
    );
    RETURN NEW;
END;
$$;
CREATE TRIGGER trg_object_history_version
BEFORE INSERT ON governance.object_history
FOR EACH ROW EXECUTE FUNCTION governance.reserve_object_history_version();

CREATE TABLE governance.object_tombstones (
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    object_id uuid NOT NULL,
    object_type varchar(40) NOT NULL,
    last_object_version bigint NOT NULL CHECK (last_object_version > 0),
    purged_at timestamptz NOT NULL,
    purge_correlation_id uuid NOT NULL,
    legal_hold_released_at timestamptz,
    PRIMARY KEY (organization_id, object_id)
);

CREATE TABLE governance.history_redactions (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    object_id uuid NOT NULL,
    requested_by uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    redacted_paths text[] NOT NULL CHECK (cardinality(redacted_paths) > 0),
    reason_code varchar(80) NOT NULL,
    correlation_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE INDEX ix_history_redactions_object
    ON governance.history_redactions (organization_id, object_id, created_at DESC);

CREATE OR REPLACE FUNCTION governance.reject_append_only_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'APPEND_ONLY_RELATION' USING ERRCODE = '42501';
END;
$$;
CREATE TRIGGER trg_audit_entries_append_only
BEFORE UPDATE OR DELETE ON governance.audit_entries
FOR EACH ROW EXECUTE FUNCTION governance.reject_append_only_mutation();
CREATE TRIGGER trg_object_history_append_only
BEFORE UPDATE OR DELETE ON governance.object_history
FOR EACH ROW EXECUTE FUNCTION governance.reject_append_only_mutation();
CREATE TRIGGER trg_history_redactions_append_only
BEFORE UPDATE OR DELETE ON governance.history_redactions
FOR EACH ROW EXECUTE FUNCTION governance.reject_append_only_mutation();

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'organizer_runtime') THEN
        CREATE ROLE organizer_runtime NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'organizer_history_writer') THEN
        CREATE ROLE organizer_history_writer NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
    END IF;
END;
$$;
REVOKE ALL ON governance.audit_entries, governance.object_history, governance.history_redactions
    FROM PUBLIC, organizer_runtime, organizer_history_writer;
GRANT SELECT ON governance.audit_entries, governance.object_history, governance.history_redactions
    TO organizer_runtime;
GRANT INSERT, SELECT ON governance.audit_entries, governance.object_history, governance.history_redactions
    TO organizer_history_writer;
GRANT USAGE ON SCHEMA governance TO organizer_runtime, organizer_history_writer;

-- Stable lock ordering for all mutable graphs.
CREATE OR REPLACE FUNCTION core.lock_graph_nodes(target_organization_id uuid, node_ids uuid[])
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    node_id uuid;
BEGIN
    FOR node_id IN
        SELECT DISTINCT value
        FROM unnest(node_ids) AS value
        WHERE value IS NOT NULL
        ORDER BY value
    LOOP
        PERFORM pg_advisory_xact_lock(
            hashtextextended(target_organization_id::text || ':' || node_id::text, 0)
        );
    END LOOP;
END;
$$;

DROP TRIGGER trg_tasks_parent_depth ON work.tasks;
CREATE OR REPLACE FUNCTION work.enforce_task_parent_depth()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    parent_parent uuid;
    parent_org uuid;
    parent_project uuid;
BEGIN
    PERFORM core.lock_graph_nodes(
        NEW.organization_id,
        ARRAY[NEW.id, NEW.parent_task_id, CASE WHEN TG_OP = 'UPDATE' THEN OLD.parent_task_id END]
    );
    IF NEW.parent_task_id IS NULL THEN
        RETURN NEW;
    END IF;
    SELECT parent_task_id, organization_id, project_id
      INTO parent_parent, parent_org, parent_project
      FROM work.tasks
     WHERE id = NEW.parent_task_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'TASK_PARENT_NOT_FOUND' USING ERRCODE = '23503';
    END IF;
    IF parent_parent IS NOT NULL THEN
        RAISE EXCEPTION 'TASK_MAX_DEPTH_EXCEEDED' USING ERRCODE = '23514';
    END IF;
    IF parent_org <> NEW.organization_id OR parent_project IS DISTINCT FROM NEW.project_id THEN
        RAISE EXCEPTION 'TASK_PARENT_SCOPE_MISMATCH' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER trg_tasks_parent_depth
BEFORE INSERT OR UPDATE OF parent_task_id, organization_id, project_id ON work.tasks
FOR EACH ROW EXECUTE FUNCTION work.enforce_task_parent_depth();

DROP TRIGGER trg_catalog_parent ON files.catalog_items;
CREATE OR REPLACE FUNCTION files.enforce_catalog_parent()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    parent_type varchar(24);
    parent_org uuid;
BEGIN
    PERFORM core.lock_graph_nodes(
        NEW.organization_id,
        ARRAY[NEW.id, NEW.parent_item_id, CASE WHEN TG_OP = 'UPDATE' THEN OLD.parent_item_id END]
    );
    IF NEW.parent_item_id IS NULL THEN
        RETURN NEW;
    END IF;
    SELECT item_type, organization_id
      INTO parent_type, parent_org
      FROM files.catalog_items
     WHERE id = NEW.parent_item_id;
    IF NOT FOUND OR parent_type <> 'virtual_folder' OR parent_org <> NEW.organization_id THEN
        RAISE EXCEPTION 'CATALOG_PARENT_INVALID' USING ERRCODE = '23514';
    END IF;
    IF EXISTS (
        WITH RECURSIVE ancestors(id, parent_item_id) AS (
            SELECT id, parent_item_id
            FROM files.catalog_items
            WHERE id = NEW.parent_item_id
            UNION
            SELECT parent.id, parent.parent_item_id
            FROM files.catalog_items AS parent
            JOIN ancestors ON parent.id = ancestors.parent_item_id
        )
        SELECT 1 FROM ancestors WHERE id = NEW.id
    ) THEN
        RAISE EXCEPTION 'CATALOG_PARENT_CYCLE' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER trg_catalog_parent
BEFORE INSERT OR UPDATE OF parent_item_id, organization_id ON files.catalog_items
FOR EACH ROW EXECUTE FUNCTION files.enforce_catalog_parent();

CREATE OR REPLACE FUNCTION work.enforce_task_dependency_graph()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    predecessor_org uuid;
    successor_org uuid;
BEGIN
    SELECT organization_id INTO predecessor_org FROM work.tasks WHERE id = NEW.predecessor_task_id;
    SELECT organization_id INTO successor_org FROM work.tasks WHERE id = NEW.successor_task_id;
    IF predecessor_org IS NULL OR successor_org IS NULL OR predecessor_org <> successor_org THEN
        RAISE EXCEPTION 'TASK_DEPENDENCY_SCOPE_MISMATCH' USING ERRCODE = '23514';
    END IF;
    PERFORM core.lock_graph_nodes(
        predecessor_org,
        ARRAY[NEW.predecessor_task_id, NEW.successor_task_id]
    );
    IF EXISTS (
        WITH RECURSIVE successors(task_id) AS (
            SELECT successor_task_id
            FROM work.task_dependencies
            WHERE predecessor_task_id = NEW.successor_task_id
            UNION
            SELECT dependency.successor_task_id
            FROM work.task_dependencies AS dependency
            JOIN successors ON dependency.predecessor_task_id = successors.task_id
        )
        SELECT 1 FROM successors WHERE task_id = NEW.predecessor_task_id
    ) THEN
        RAISE EXCEPTION 'TASK_DEPENDENCY_CYCLE' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER trg_task_dependency_graph
BEFORE INSERT OR UPDATE ON work.task_dependencies
FOR EACH ROW EXECUTE FUNCTION work.enforce_task_dependency_graph();

-- Every single-column tenant FK is checked against the child's organization.
CREATE OR REPLACE FUNCTION core.enforce_tenant_reference()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    reference_id uuid;
    reference_organization_id uuid;
BEGIN
    reference_id := nullif(to_jsonb(NEW) ->> TG_ARGV[0], '')::uuid;
    IF reference_id IS NULL THEN
        RETURN NEW;
    END IF;
    EXECUTE format(
        'SELECT organization_id FROM %I.%I WHERE %I = $1',
        TG_ARGV[1], TG_ARGV[2], TG_ARGV[3]
    )
    INTO reference_organization_id
    USING reference_id;
    IF reference_organization_id IS NOT NULL
       AND reference_organization_id IS DISTINCT FROM NEW.organization_id THEN
        RAISE EXCEPTION 'TENANT_REFERENCE_MISMATCH: %.%', TG_TABLE_SCHEMA, TG_TABLE_NAME
            USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION core.enforce_inferred_tenant_references()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    argument_index integer;
    reference_id uuid;
    reference_organization_id uuid;
    inferred_organization_id uuid;
BEGIN
    argument_index := 0;
    WHILE argument_index < TG_NARGS LOOP
        reference_id := nullif(to_jsonb(NEW) ->> TG_ARGV[argument_index], '')::uuid;
        IF reference_id IS NOT NULL THEN
            EXECUTE format(
                'SELECT organization_id FROM %I.%I WHERE %I = $1',
                TG_ARGV[argument_index + 1],
                TG_ARGV[argument_index + 2],
                TG_ARGV[argument_index + 3]
            )
            INTO reference_organization_id
            USING reference_id;
            IF reference_organization_id IS NOT NULL THEN
                IF inferred_organization_id IS NULL THEN
                    inferred_organization_id := reference_organization_id;
                ELSIF inferred_organization_id <> reference_organization_id THEN
                    RAISE EXCEPTION 'INFERRED_TENANT_REFERENCE_MISMATCH: %.%', TG_TABLE_SCHEMA, TG_TABLE_NAME
                        USING ERRCODE = '23514';
                END IF;
            END IF;
        END IF;
        argument_index := argument_index + 4;
    END LOOP;
    RETURN NEW;
END;
$$;

DO $$
DECLARE
    relation_record record;
    trigger_name text;
    trigger_arguments text;
BEGIN
    FOR relation_record IN
        SELECT
            constraint_row.oid AS constraint_oid,
            child_namespace.nspname AS child_schema,
            child_class.relname AS child_table,
            child_attribute.attname AS child_column,
            parent_namespace.nspname AS parent_schema,
            parent_class.relname AS parent_table,
            parent_attribute.attname AS parent_column
        FROM pg_constraint AS constraint_row
        JOIN pg_class AS child_class ON child_class.oid = constraint_row.conrelid
        JOIN pg_namespace AS child_namespace ON child_namespace.oid = child_class.relnamespace
        JOIN pg_class AS parent_class ON parent_class.oid = constraint_row.confrelid
        JOIN pg_namespace AS parent_namespace ON parent_namespace.oid = parent_class.relnamespace
        JOIN pg_attribute AS child_attribute
          ON child_attribute.attrelid = constraint_row.conrelid
         AND child_attribute.attnum = constraint_row.conkey[1]
        JOIN pg_attribute AS parent_attribute
          ON parent_attribute.attrelid = constraint_row.confrelid
         AND parent_attribute.attnum = constraint_row.confkey[1]
        WHERE constraint_row.contype = 'f'
          AND cardinality(constraint_row.conkey) = 1
          AND child_attribute.attname <> 'organization_id'
          AND EXISTS (
              SELECT 1 FROM pg_attribute
              WHERE attrelid = constraint_row.conrelid
                AND attname = 'organization_id' AND NOT attisdropped
          )
          AND EXISTS (
              SELECT 1 FROM pg_attribute
              WHERE attrelid = constraint_row.confrelid
                AND attname = 'organization_id' AND NOT attisdropped
          )
    LOOP
        trigger_name := 'trg_tenant_' || substr(md5(relation_record.constraint_oid::text), 1, 16);
        EXECUTE format(
            'CREATE TRIGGER %I BEFORE INSERT OR UPDATE ON %I.%I FOR EACH ROW EXECUTE FUNCTION core.enforce_tenant_reference(%L,%L,%L,%L)',
            trigger_name,
            relation_record.child_schema,
            relation_record.child_table,
            relation_record.child_column,
            relation_record.parent_schema,
            relation_record.parent_table,
            relation_record.parent_column
        );
    END LOOP;

    FOR relation_record IN
        SELECT
            child_namespace.nspname AS child_schema,
            child_class.relname AS child_table,
            string_agg(
                format(
                    '%L,%L,%L,%L',
                    child_attribute.attname,
                    parent_namespace.nspname,
                    parent_class.relname,
                    parent_attribute.attname
                ),
                ',' ORDER BY constraint_row.oid
            ) AS arguments
        FROM pg_constraint AS constraint_row
        JOIN pg_class AS child_class ON child_class.oid = constraint_row.conrelid
        JOIN pg_namespace AS child_namespace ON child_namespace.oid = child_class.relnamespace
        JOIN pg_class AS parent_class ON parent_class.oid = constraint_row.confrelid
        JOIN pg_namespace AS parent_namespace ON parent_namespace.oid = parent_class.relnamespace
        JOIN pg_attribute AS child_attribute
          ON child_attribute.attrelid = constraint_row.conrelid
         AND child_attribute.attnum = constraint_row.conkey[1]
        JOIN pg_attribute AS parent_attribute
          ON parent_attribute.attrelid = constraint_row.confrelid
         AND parent_attribute.attnum = constraint_row.confkey[1]
        WHERE constraint_row.contype = 'f'
          AND cardinality(constraint_row.conkey) = 1
          AND NOT EXISTS (
              SELECT 1 FROM pg_attribute
              WHERE attrelid = constraint_row.conrelid
                AND attname = 'organization_id' AND NOT attisdropped
          )
          AND EXISTS (
              SELECT 1 FROM pg_attribute
              WHERE attrelid = constraint_row.confrelid
                AND attname = 'organization_id' AND NOT attisdropped
          )
        GROUP BY child_namespace.nspname, child_class.relname, constraint_row.conrelid
        HAVING count(*) >= 2
    LOOP
        trigger_name := 'trg_inferred_tenant_' || substr(
            md5(relation_record.child_schema || '.' || relation_record.child_table), 1, 12
        );
        trigger_arguments := relation_record.arguments;
        EXECUTE format(
            'CREATE TRIGGER %I BEFORE INSERT OR UPDATE ON %I.%I FOR EACH ROW EXECUTE FUNCTION core.enforce_inferred_tenant_references(%s)',
            trigger_name,
            relation_record.child_schema,
            relation_record.child_table,
            trigger_arguments
        );
    END LOOP;
END;
$$;

-- Add only missing leading indexes for foreign-key lookup paths.
DO $$
DECLARE
    constraint_record record;
    index_name text;
BEGIN
    FOR constraint_record IN
        SELECT
            constraint_row.oid,
            namespace_row.nspname AS schema_name,
            class_row.relname AS table_name,
            constraint_row.conrelid,
            constraint_row.conkey,
            string_agg(quote_ident(attribute_row.attname), ',' ORDER BY key_row.ordinality) AS columns_sql
        FROM pg_constraint AS constraint_row
        JOIN pg_class AS class_row ON class_row.oid = constraint_row.conrelid
        JOIN pg_namespace AS namespace_row ON namespace_row.oid = class_row.relnamespace
        JOIN unnest(constraint_row.conkey) WITH ORDINALITY AS key_row(attnum, ordinality) ON true
        JOIN pg_attribute AS attribute_row
          ON attribute_row.attrelid = constraint_row.conrelid
         AND attribute_row.attnum = key_row.attnum
        WHERE constraint_row.contype = 'f'
          AND class_row.relkind IN ('r','p')
        GROUP BY constraint_row.oid, namespace_row.nspname, class_row.relname,
                 constraint_row.conrelid, constraint_row.conkey
        HAVING NOT EXISTS (
            SELECT 1
            FROM pg_index AS index_row
            WHERE index_row.indrelid = constraint_row.conrelid
              AND (
                  SELECT array_agg(index_key.attnum ORDER BY index_key.ordinality)
                  FROM unnest(index_row.indkey) WITH ORDINALITY
                       AS index_key(attnum, ordinality)
                  WHERE index_key.ordinality <= cardinality(constraint_row.conkey)
              ) = constraint_row.conkey::smallint[]
        )
    LOOP
        index_name := 'ix_fk_' || substr(md5(constraint_record.oid::text), 1, 16);
        EXECUTE format(
            'CREATE INDEX %I ON %I.%I (%s)',
            index_name,
            constraint_record.schema_name,
            constraint_record.table_name,
            constraint_record.columns_sql
        );
    END LOOP;
END;
$$;

-- Today is a first-class read model; the API filters local_date in the user's time zone.
CREATE VIEW calendar.today_read_model AS
SELECT
    schedule.organization_id,
    schedule.object_id,
    schedule.item_type,
    schedule.title,
    schedule.local_date,
    schedule.start_at_utc,
    schedule.end_at_utc,
    schedule.is_all_day,
    schedule.project_id,
    schedule.status,
    schedule.priority,
    NULL::uuid AS recipient_user_id
FROM calendar.schedule_items AS schedule
UNION ALL
SELECT
    reminder.organization_id,
    reminder.id,
    'reminder'::text,
    'Reminder'::text,
    (reminder.next_trigger_at AT TIME ZONE 'UTC')::date,
    reminder.next_trigger_at,
    NULL::timestamptz,
    false,
    NULL::uuid,
    reminder.status,
    NULL::varchar,
    reminder.recipient_user_id
FROM calendar.reminders AS reminder
WHERE reminder.status IN ('scheduled','due','snoozed');

-- Bounded retention for high-volume operational data.
CREATE TABLE ops.retention_policies (
    relation_name text PRIMARY KEY,
    retention_days integer NOT NULL CHECK (retention_days BETWEEN 1 AND 36500),
    cleanup_batch_size integer NOT NULL DEFAULT 10000 CHECK (cleanup_batch_size BETWEEN 100 AND 100000),
    partition_strategy varchar(20) NOT NULL
        CHECK (partition_strategy IN ('monthly','weekly','bounded_delete')),
    legal_hold_supported boolean NOT NULL DEFAULT false,
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
INSERT INTO ops.retention_policies (
    relation_name, retention_days, cleanup_batch_size, partition_strategy, legal_hold_supported
) VALUES
    ('governance.audit_entries', 1095, 10000, 'monthly', true),
    ('governance.object_history', 1095, 10000, 'monthly', true),
    ('iam.login_attempts', 180, 10000, 'bounded_delete', false),
    ('files.file_location_checks', 90, 10000, 'bounded_delete', false),
    ('notify.notification_deliveries', 90, 10000, 'bounded_delete', false),
    ('ops.background_job_runs', 180, 10000, 'bounded_delete', false),
    ('iam.idempotency_records', 7, 10000, 'bounded_delete', false),
    ('sync.snapshot_sessions', 2, 10000, 'bounded_delete', false);

COMMIT;
