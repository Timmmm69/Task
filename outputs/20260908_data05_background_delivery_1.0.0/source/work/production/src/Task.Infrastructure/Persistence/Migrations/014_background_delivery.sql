CREATE SCHEMA IF NOT EXISTS sync;

ALTER TABLE governance.outbox_messages
    ADD COLUMN lock_token uuid,
    ADD COLUMN lease_expires_at timestamptz,
    ADD COLUMN heartbeat_at timestamptz,
    ADD COLUMN next_attempt_at timestamptz NOT NULL DEFAULT clock_timestamp();

UPDATE governance.outbox_messages
   SET status = 'failed', next_attempt_at = clock_timestamp(), locked_by = NULL, locked_at = NULL
 WHERE status = 'processing';

ALTER TABLE governance.outbox_messages
    ADD CONSTRAINT ck_outbox_lease CHECK (
        (status = 'processing' AND locked_by IS NOT NULL AND lock_token IS NOT NULL
            AND lease_expires_at IS NOT NULL AND heartbeat_at IS NOT NULL)
        OR status <> 'processing');

DROP INDEX governance.ix_outbox_claim;
CREATE INDEX ix_outbox_claim
    ON governance.outbox_messages (status, next_attempt_at, available_at, created_at)
    WHERE status IN ('pending','failed') OR status = 'processing';

CREATE SEQUENCE sync.change_sequence AS bigint START WITH 1 INCREMENT BY 1 NO CYCLE;

CREATE TABLE sync.change_feed (
    sequence bigint PRIMARY KEY DEFAULT nextval('sync.change_sequence'),
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    source_event_id uuid NOT NULL REFERENCES governance.domain_events(id) ON DELETE RESTRICT,
    object_id uuid NOT NULL,
    object_type varchar(40) NOT NULL,
    operation varchar(16) NOT NULL
        CHECK (operation IN ('upsert','tombstone','scope_revoke')),
    object_version bigint NOT NULL CHECK (object_version > 0),
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    permission_scope_version bigint NOT NULL CHECK (permission_scope_version > 0),
    changed_fields text[] NOT NULL DEFAULT '{}',
    payload_hint jsonb NOT NULL DEFAULT '{}'::jsonb,
    correlation_id uuid NOT NULL,
    CONSTRAINT ck_change_feed_object_type CHECK (length(btrim(object_type)) BETWEEN 1 AND 40),
    CONSTRAINT ck_change_feed_payload_hint CHECK (jsonb_typeof(payload_hint) = 'object')
);
CREATE INDEX ix_change_feed_org_sequence ON sync.change_feed (organization_id, sequence);
CREATE INDEX ix_change_feed_object ON sync.change_feed (organization_id, object_id, sequence DESC);
CREATE INDEX ix_change_feed_time ON sync.change_feed (occurred_at);
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
        organization_id, source_event_id, object_id, object_type, operation,
        object_version, occurred_at, permission_scope_version, changed_fields,
        payload_hint, correlation_id)
    VALUES (
        event_row.organization_id, event_row.id, changed_object_id, changed_object_type,
        change_operation, changed_object_version, event_row.occurred_at,
        changed_permission_scope_version, coalesce(changed_field_names, '{}'),
        coalesce(hint, '{}'::jsonb), event_row.correlation_id)
    ON CONFLICT (organization_id, source_event_id, object_id, operation)
    DO UPDATE SET source_event_id = EXCLUDED.source_event_id
    RETURNING sequence INTO projected_sequence;

    RETURN projected_sequence;
END;
$$;

CREATE TABLE calendar.reminders (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    target_object_id uuid NOT NULL,
    recipient_user_id uuid NOT NULL,
    trigger_type varchar(24) NOT NULL
        CHECK (trigger_type IN ('absolute','before_start','before_deadline','at_start','at_deadline')),
    offset_minutes integer,
    absolute_trigger_at timestamptz,
    next_trigger_at timestamptz NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'scheduled'
        CHECK (status IN ('scheduled','due','delivered','snoozed','cancelled','expired')),
    snooze_count integer NOT NULL DEFAULT 0 CHECK (snooze_count BETWEEN 0 AND 100),
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    created_by uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_reminders_org_id UNIQUE (organization_id, id),
    CONSTRAINT fk_reminder_target_org FOREIGN KEY (organization_id, target_object_id)
        REFERENCES core.objects(organization_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_reminder_recipient_org FOREIGN KEY (organization_id, recipient_user_id)
        REFERENCES iam.user_accounts(organization_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_reminder_creator_org FOREIGN KEY (organization_id, created_by)
        REFERENCES iam.user_accounts(organization_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_reminder_trigger CHECK (
        (trigger_type = 'absolute' AND absolute_trigger_at IS NOT NULL AND offset_minutes IS NULL) OR
        (trigger_type IN ('before_start','before_deadline') AND absolute_trigger_at IS NULL
            AND offset_minutes BETWEEN 0 AND 525600) OR
        (trigger_type IN ('at_start','at_deadline') AND absolute_trigger_at IS NULL AND offset_minutes IS NULL)),
    CONSTRAINT ck_reminder_timestamps CHECK (updated_at >= created_at)
);
CREATE INDEX ix_reminders_due
    ON calendar.reminders (next_trigger_at, id) WHERE status IN ('scheduled','snoozed');
CREATE INDEX ix_reminders_recipient
    ON calendar.reminders (organization_id, recipient_user_id, status, next_trigger_at);

CREATE TABLE calendar.reminder_occurrences (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    reminder_id uuid NOT NULL,
    due_at timestamptz NOT NULL,
    status varchar(16) NOT NULL DEFAULT 'created'
        CHECK (status IN ('created','claimed','delivered','failed','dead_letter','cancelled')),
    claimed_by_worker text,
    claimed_at timestamptz,
    delivered_at timestamptz,
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count BETWEEN 0 AND 10),
    next_attempt_at timestamptz NOT NULL,
    lock_token uuid,
    lease_expires_at timestamptz,
    heartbeat_at timestamptz,
    last_error_code varchar(80),
    idempotency_key varchar(160) NOT NULL UNIQUE,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT fk_reminder_occurrence_org FOREIGN KEY (organization_id, reminder_id)
        REFERENCES calendar.reminders(organization_id, id) ON DELETE CASCADE,
    CONSTRAINT uq_reminder_occurrence UNIQUE (reminder_id, due_at),
    CONSTRAINT ck_reminder_occurrence_lease CHECK (
        (status = 'claimed' AND claimed_by_worker IS NOT NULL AND claimed_at IS NOT NULL
            AND lock_token IS NOT NULL AND lease_expires_at IS NOT NULL AND heartbeat_at IS NOT NULL)
        OR status <> 'claimed'),
    CONSTRAINT ck_reminder_occurrence_delivery CHECK (
        (status = 'delivered' AND delivered_at IS NOT NULL) OR status <> 'delivered')
);
CREATE INDEX ix_reminder_occurrences_claim
    ON calendar.reminder_occurrences (status, next_attempt_at, due_at, id)
    WHERE status IN ('created','failed') OR status = 'claimed';
