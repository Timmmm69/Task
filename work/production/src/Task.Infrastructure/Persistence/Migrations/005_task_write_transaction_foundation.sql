CREATE TABLE iam.idempotency_records (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL,
    user_account_id uuid NOT NULL,
    operation_id varchar(160) NOT NULL,
    idempotency_key varchar(200) NOT NULL,
    request_hash bytea NOT NULL,
    state varchar(16) NOT NULL DEFAULT 'in_progress',
    lease_owner uuid,
    lease_expires_at timestamptz,
    response_status smallint,
    response_headers jsonb,
    response_body jsonb,
    resource_id uuid,
    failure_code varchar(128),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    completed_at timestamptz,
    retention_expires_at timestamptz NOT NULL,
    CONSTRAINT fk_idempotency_user_tenant FOREIGN KEY (organization_id, user_account_id)
        REFERENCES iam.user_accounts(organization_id, id) ON DELETE RESTRICT,
    CONSTRAINT uq_idempotency_scope
        UNIQUE (organization_id, user_account_id, operation_id, idempotency_key),
    CONSTRAINT ck_idempotency_operation CHECK (length(btrim(operation_id)) BETWEEN 1 AND 160),
    CONSTRAINT ck_idempotency_key CHECK (idempotency_key ~ '^[!-~]{8,200}$'),
    CONSTRAINT ck_idempotency_hash CHECK (octet_length(request_hash) = 32),
    CONSTRAINT ck_idempotency_state CHECK (state IN ('in_progress', 'completed', 'failed')),
    CONSTRAINT ck_idempotency_status CHECK (response_status BETWEEN 100 AND 599),
    CONSTRAINT ck_idempotency_headers CHECK (
        response_headers IS NULL OR jsonb_typeof(response_headers) = 'object'),
    CONSTRAINT ck_idempotency_retention CHECK (retention_expires_at > created_at),
    CONSTRAINT ck_idempotency_lease CHECK (
        (state = 'in_progress' AND lease_owner IS NOT NULL AND lease_expires_at IS NOT NULL)
        OR state <> 'in_progress'),
    CONSTRAINT ck_idempotency_completed CHECK (
        (state = 'completed' AND response_status IS NOT NULL AND response_headers IS NOT NULL
            AND response_body IS NOT NULL AND completed_at IS NOT NULL
            AND lease_owner IS NULL AND lease_expires_at IS NULL)
        OR state <> 'completed')
);

CREATE INDEX ix_idempotency_retention
    ON iam.idempotency_records (retention_expires_at);
CREATE INDEX ix_idempotency_active_lease
    ON iam.idempotency_records (lease_expires_at)
    WHERE state = 'in_progress';

CREATE TABLE governance.domain_events (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    aggregate_id uuid NOT NULL,
    aggregate_type varchar(40) NOT NULL,
    aggregate_version bigint NOT NULL,
    event_type varchar(100) NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    actor_user_id uuid NOT NULL,
    correlation_id uuid NOT NULL,
    operation_id varchar(160) NOT NULL,
    idempotency_key varchar(200) NOT NULL,
    changed_fields text[] NOT NULL DEFAULT '{}',
    payload jsonb NOT NULL,
    schema_version smallint NOT NULL DEFAULT 1,
    CONSTRAINT fk_domain_event_actor_tenant FOREIGN KEY (organization_id, actor_user_id)
        REFERENCES iam.user_accounts(organization_id, id) ON DELETE RESTRICT,
    CONSTRAINT uq_domain_events_org_id UNIQUE (organization_id, id),
    CONSTRAINT uq_domain_event_idempotency
        UNIQUE (organization_id, actor_user_id, operation_id, idempotency_key),
    CONSTRAINT uq_domain_event_aggregate_version
        UNIQUE (organization_id, aggregate_id, aggregate_version, event_type),
    CONSTRAINT ck_domain_event_aggregate CHECK (aggregate_type = 'task'),
    CONSTRAINT ck_domain_event_version CHECK (aggregate_version > 0 AND schema_version > 0),
    CONSTRAINT ck_domain_event_type CHECK (length(btrim(event_type)) BETWEEN 1 AND 100),
    CONSTRAINT ck_domain_event_operation CHECK (length(btrim(operation_id)) BETWEEN 1 AND 160),
    CONSTRAINT ck_domain_event_key CHECK (idempotency_key ~ '^[!-~]{8,200}$'),
    CONSTRAINT ck_domain_event_payload CHECK (jsonb_typeof(payload) = 'object')
);

CREATE INDEX ix_domain_events_aggregate
    ON governance.domain_events (organization_id, aggregate_id, aggregate_version);
CREATE INDEX ix_domain_events_type_time
    ON governance.domain_events (organization_id, event_type, occurred_at DESC);

CREATE TABLE governance.outbox_messages (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL,
    domain_event_id uuid NOT NULL,
    destination varchar(40) NOT NULL,
    message_type varchar(100) NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    available_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    status varchar(16) NOT NULL DEFAULT 'pending',
    attempt_count integer NOT NULL DEFAULT 0,
    locked_by text,
    locked_at timestamptz,
    published_at timestamptz,
    last_error_code varchar(100),
    last_error_detail text,
    CONSTRAINT fk_outbox_event_tenant FOREIGN KEY (organization_id, domain_event_id)
        REFERENCES governance.domain_events(organization_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_outbox_destination CHECK (
        destination IN ('realtime', 'background', 'search', 'notification', 'sync')),
    CONSTRAINT ck_outbox_status CHECK (
        status IN ('pending', 'processing', 'published', 'failed', 'dead_letter')),
    CONSTRAINT ck_outbox_attempts CHECK (attempt_count >= 0),
    CONSTRAINT ck_outbox_message_type CHECK (length(btrim(message_type)) BETWEEN 1 AND 100),
    CONSTRAINT ck_outbox_payload CHECK (jsonb_typeof(payload) = 'object')
);

CREATE INDEX ix_outbox_claim
    ON governance.outbox_messages (status, available_at, created_at)
    WHERE status IN ('pending', 'failed');
CREATE INDEX ix_outbox_event
    ON governance.outbox_messages (organization_id, domain_event_id);

CREATE FUNCTION iam.acquire_idempotency_record(
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
    stored_record_id uuid,
    stored_response_status smallint,
    stored_response_headers jsonb,
    stored_response_body jsonb,
    stored_resource_id uuid,
    retry_after_seconds integer
)
LANGUAGE plpgsql
AS $$
DECLARE
    existing iam.idempotency_records%ROWTYPE;
BEGIN
    IF lease_duration <= interval '0 seconds' OR retention_duration <= lease_duration THEN
        RAISE EXCEPTION 'IDEMPOTENCY_DURATION_INVALID' USING ERRCODE = '22023';
    END IF;

    INSERT INTO iam.idempotency_records (
        id, organization_id, user_account_id, operation_id, idempotency_key,
        request_hash, lease_owner, lease_expires_at, retention_expires_at)
    VALUES (
        new_record_id, target_organization_id, target_user_account_id, target_operation_id,
        target_idempotency_key, target_request_hash, request_owner,
        clock_timestamp() + lease_duration, clock_timestamp() + retention_duration)
    ON CONFLICT (organization_id, user_account_id, operation_id, idempotency_key)
    DO NOTHING;

    SELECT record_row.*
      INTO STRICT existing
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
        RETURN QUERY SELECT 'replay'::text, existing.id, existing.response_status,
            existing.response_headers, existing.response_body, existing.resource_id, NULL::integer;
        RETURN;
    END IF;

    IF existing.state = 'in_progress'
       AND existing.lease_owner IS DISTINCT FROM request_owner
       AND existing.lease_expires_at >= clock_timestamp() THEN
        RETURN QUERY SELECT 'in_progress'::text, existing.id, NULL::smallint,
            NULL::jsonb, NULL::jsonb, NULL::uuid,
            greatest(1, ceil(extract(epoch FROM existing.lease_expires_at - clock_timestamp())))::integer;
        RETURN;
    END IF;

    UPDATE iam.idempotency_records
       SET state = 'in_progress',
           lease_owner = request_owner,
           lease_expires_at = clock_timestamp() + lease_duration,
           retention_expires_at = greatest(
               retention_expires_at, clock_timestamp() + retention_duration),
           failure_code = NULL
     WHERE id = existing.id;

    RETURN QUERY SELECT 'execute'::text, existing.id, NULL::smallint,
        NULL::jsonb, NULL::jsonb, NULL::uuid, NULL::integer;
END;
$$;

CREATE FUNCTION iam.complete_idempotency_record(
    target_record_id uuid,
    target_organization_id uuid,
    target_user_account_id uuid,
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
    IF result_status < 100 OR result_status > 599
       OR jsonb_typeof(coalesce(result_headers, '{}'::jsonb)) <> 'object'
       OR result_body IS NULL THEN
        RAISE EXCEPTION 'IDEMPOTENCY_RESPONSE_INVALID' USING ERRCODE = '22023';
    END IF;

    UPDATE iam.idempotency_records
       SET state = 'completed',
           response_status = result_status,
           response_headers = coalesce(result_headers, '{}'::jsonb),
           response_body = result_body,
           resource_id = result_resource_id,
           completed_at = clock_timestamp(),
           lease_owner = NULL,
           lease_expires_at = NULL,
           failure_code = NULL
     WHERE id = target_record_id
       AND organization_id = target_organization_id
       AND user_account_id = target_user_account_id
       AND state = 'in_progress'
       AND lease_owner = request_owner
       AND request_hash = target_request_hash;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'IDEMPOTENCY_LEASE_OR_HASH_MISMATCH' USING ERRCODE = '40001';
    END IF;
END;
$$;
