\set ON_ERROR_STOP on
\echo 'Organizer Stage 2.1 database contract tests'

CREATE OR REPLACE FUNCTION pg_temp.assert_true(condition boolean, message text)
RETURNS void
LANGUAGE plpgsql
AS $$
BEGIN
    IF condition IS DISTINCT FROM true THEN
        RAISE EXCEPTION 'ASSERTION_FAILED: %', message;
    END IF;
END;
$$;

SELECT pg_temp.assert_true(
    current_setting('server_version_num')::integer >= 160000,
    'PostgreSQL 16 or newer is required'
);
SELECT pg_temp.assert_true(
    (SELECT count(*) FROM iam.permissions) = 91,
    'canonical permission count must be 91'
);
SELECT pg_temp.assert_true(
    (SELECT count(*) FROM iam.permissions) = (SELECT count(DISTINCT code) FROM iam.permissions),
    'permission codes must be unique'
);
SELECT pg_temp.assert_true(
    (
        SELECT pg_get_expr(attribute_row.adbin, attribute_row.adrelid)
        FROM pg_attrdef AS attribute_row
        JOIN pg_attribute AS column_row
          ON column_row.attrelid = attribute_row.adrelid
         AND column_row.attnum = attribute_row.adnum
        WHERE attribute_row.adrelid = 'iam.user_accounts'::regclass
          AND column_row.attname = 'account_status'
    ) = '''pending_activation''::character varying',
    'new user default must be pending_activation'
);

SELECT iam.bootstrap_first_administrator(
    '01900000-0000-7000-8000-000000000001',
    'validation-a',
    'Validation Organization A',
    'Europe/Minsk',
    '01900000-0000-7000-8000-000000000002',
    '01900000-0000-7000-8000-000000000003',
    'admin.a',
    '$argon2id$v=19$m=65536,t=3,p=1$validation$validation',
    'Admin',
    'A'
);
SELECT iam.bootstrap_first_administrator(
    '01900000-0000-7000-8000-000000000001',
    'validation-a',
    'Validation Organization A',
    'Europe/Minsk',
    '01900000-0000-7000-8000-000000000002',
    '01900000-0000-7000-8000-000000000003',
    'admin.a',
    '$argon2id$v=19$m=65536,t=3,p=1$validation$validation',
    'Admin',
    'A'
);

SELECT pg_temp.assert_true(
    (SELECT count(*) FROM iam.roles WHERE organization_id = '01900000-0000-7000-8000-000000000001') = 4,
    'four canonical system roles must be created'
);
SELECT pg_temp.assert_true(
    (SELECT count(*) FROM projects.project_roles WHERE organization_id = '01900000-0000-7000-8000-000000000001') = 5,
    'five canonical project roles must be created'
);
SELECT pg_temp.assert_true(
    (
        SELECT count(*)
        FROM iam.role_permissions AS role_permission
        JOIN iam.roles AS role_row ON role_row.id = role_permission.role_id
        WHERE role_row.organization_id = '01900000-0000-7000-8000-000000000001'
          AND role_row.code = 'system_admin'
    ) = 91,
    'system_admin must receive every canonical permission'
);
SELECT pg_temp.assert_true(
    (
        SELECT count(*)
        FROM iam.user_roles AS assignment
        JOIN iam.roles AS role_row ON role_row.id = assignment.role_id
        WHERE assignment.user_account_id = '01900000-0000-7000-8000-000000000003'
          AND role_row.code = 'system_admin'
    ) = 1,
    'bootstrap account must be system_admin exactly once'
);

SELECT iam.bootstrap_first_administrator(
    '01900000-0000-7000-8000-000000000011',
    'validation-b',
    'Validation Organization B',
    'Europe/Minsk',
    '01900000-0000-7000-8000-000000000012',
    '01900000-0000-7000-8000-000000000013',
    'admin.b',
    '$argon2id$v=19$m=65536,t=3,p=1$validation$validation',
    'Admin',
    'B'
);

DO $$
BEGIN
    INSERT INTO core.objects (id, organization_id, object_type, created_by, updated_by)
    VALUES (
        '01900000-0000-7000-8000-000000000021',
        '01900000-0000-7000-8000-000000000001',
        'project',
        '01900000-0000-7000-8000-000000000003',
        '01900000-0000-7000-8000-000000000003'
    );
    BEGIN
        INSERT INTO projects.projects (
            id, organization_id, name, owner_user_id
        )
        VALUES (
            '01900000-0000-7000-8000-000000000021',
            '01900000-0000-7000-8000-000000000001',
            'Cross-tenant project',
            '01900000-0000-7000-8000-000000000013'
        );
        RAISE EXCEPTION 'cross-tenant owner was accepted';
    EXCEPTION
        WHEN check_violation THEN NULL;
    END;
END;
$$;

INSERT INTO iam.idempotency_records (
    id, organization_id, user_account_id, operation_id, idempotency_key,
    request_hash, expires_at
)
VALUES (
    '01900000-0000-7000-8000-000000000031',
    '01900000-0000-7000-8000-000000000001',
    '01900000-0000-7000-8000-000000000003',
    'Tasks.Create',
    'validation-key',
    decode(repeat('aa', 32), 'hex'),
    clock_timestamp() + interval '1 day'
);
DO $$
BEGIN
    BEGIN
        INSERT INTO iam.idempotency_records (
            id, organization_id, user_account_id, operation_id, idempotency_key,
            request_hash, expires_at
        )
        VALUES (
            '01900000-0000-7000-8000-000000000032',
            '01900000-0000-7000-8000-000000000001',
            '01900000-0000-7000-8000-000000000003',
            'Tasks.Create',
            'validation-key',
            decode(repeat('bb', 32), 'hex'),
            clock_timestamp() + interval '1 day'
        );
        RAISE EXCEPTION 'idempotency scope collision was accepted';
    EXCEPTION
        WHEN unique_violation THEN NULL;
    END;
END;
$$;

DO $$
DECLARE
    acquisition record;
BEGIN
    SELECT * INTO acquisition
    FROM iam.acquire_idempotency_record(
        '01900000-0000-7000-8000-000000000033',
        '01900000-0000-7000-8000-000000000001',
        '01900000-0000-7000-8000-000000000003',
        'Projects.Create',
        'pipeline-key',
        decode(repeat('cc', 32), 'hex'),
        '01900000-0000-7000-8000-000000000034',
        interval '1 minute',
        interval '1 day'
    );
    IF acquisition.disposition <> 'execute' THEN
        RAISE EXCEPTION 'first idempotency acquisition must execute';
    END IF;

    PERFORM iam.complete_idempotency_record(
        '01900000-0000-7000-8000-000000000033',
        '01900000-0000-7000-8000-000000000034',
        decode(repeat('cc', 32), 'hex'),
        201,
        '{"ETag":"\"v1\""}',
        '{"id":"01900000-0000-7000-8000-000000000021"}',
        '01900000-0000-7000-8000-000000000021'
    );

    SELECT * INTO acquisition
    FROM iam.acquire_idempotency_record(
        '01900000-0000-7000-8000-000000000035',
        '01900000-0000-7000-8000-000000000001',
        '01900000-0000-7000-8000-000000000003',
        'Projects.Create',
        'pipeline-key',
        decode(repeat('cc', 32), 'hex'),
        '01900000-0000-7000-8000-000000000036',
        interval '1 minute',
        interval '1 day'
    );
    IF acquisition.disposition <> 'replay' OR acquisition.stored_response_status <> 201 THEN
        RAISE EXCEPTION 'completed idempotency result must replay';
    END IF;

    BEGIN
        PERFORM *
        FROM iam.acquire_idempotency_record(
            '01900000-0000-7000-8000-000000000037',
            '01900000-0000-7000-8000-000000000001',
            '01900000-0000-7000-8000-000000000003',
            'Projects.Create',
            'pipeline-key',
            decode(repeat('dd', 32), 'hex'),
            '01900000-0000-7000-8000-000000000038',
            interval '1 minute',
            interval '1 day'
        );
        RAISE EXCEPTION 'different idempotency request hash was accepted';
    EXCEPTION
        WHEN check_violation THEN NULL;
    END;
END;
$$;

INSERT INTO governance.object_history (
    id, organization_id, object_id, object_type, object_version, changed_by,
    change_type, snapshot, correlation_id
)
VALUES (
    '01900000-0000-7000-8000-000000000041',
    '01900000-0000-7000-8000-000000000001',
    '01900000-0000-7000-8000-000000000003',
    'user_account',
    1,
    '01900000-0000-7000-8000-000000000003',
    'created',
    '{"status":"active"}',
    '01900000-0000-7000-8000-000000000042'
);
DO $$
BEGIN
    BEGIN
        INSERT INTO governance.object_history (
            id, organization_id, object_id, object_type, object_version, changed_by,
            change_type, snapshot, correlation_id
        )
        VALUES (
            '01900000-0000-7000-8000-000000000043',
            '01900000-0000-7000-8000-000000000001',
            '01900000-0000-7000-8000-000000000003',
            'user_account',
            1,
            '01900000-0000-7000-8000-000000000003',
            'updated',
            '{"status":"active"}',
            '01900000-0000-7000-8000-000000000044'
        );
        RAISE EXCEPTION 'duplicate object history version was accepted';
    EXCEPTION
        WHEN unique_violation THEN NULL;
    END;
    BEGIN
        UPDATE governance.object_history
        SET changed_fields = ARRAY['forbidden']
        WHERE id = '01900000-0000-7000-8000-000000000041';
        RAISE EXCEPTION 'append-only history update was accepted';
    EXCEPTION
        WHEN insufficient_privilege THEN NULL;
    END;
END;
$$;

SELECT pg_temp.assert_true(
    NOT has_table_privilege('organizer_runtime', 'governance.object_history', 'UPDATE'),
    'runtime role must not update history'
);
SELECT pg_temp.assert_true(
    NOT has_table_privilege('organizer_runtime', 'governance.audit_entries', 'DELETE'),
    'runtime role must not delete audit entries'
);

INSERT INTO governance.domain_events (
    id, organization_id, aggregate_id, aggregate_type, aggregate_version,
    event_type, actor_user_id, correlation_id, idempotency_key, payload
)
VALUES (
    '01900000-0000-7000-8000-000000000051',
    '01900000-0000-7000-8000-000000000001',
    '01900000-0000-7000-8000-000000000003',
    'user_account',
    2,
    'ValidationEvent',
    '01900000-0000-7000-8000-000000000003',
    '01900000-0000-7000-8000-000000000052',
    'validation-event',
    '{}'
);
SELECT sync.project_domain_event_change(
    '01900000-0000-7000-8000-000000000051',
    '01900000-0000-7000-8000-000000000003',
    'user_account',
    'upsert',
    2,
    1,
    ARRAY['accountStatus'],
    '{}'
);
SELECT sync.project_domain_event_change(
    '01900000-0000-7000-8000-000000000051',
    '01900000-0000-7000-8000-000000000003',
    'user_account',
    'upsert',
    2,
    1,
    ARRAY['accountStatus'],
    '{}'
);
SELECT pg_temp.assert_true(
    (
        SELECT count(*) FROM sync.change_feed
        WHERE source_event_id = '01900000-0000-7000-8000-000000000051'
    ) = 1,
    'change feed projection must deduplicate source event reprocessing'
);

INSERT INTO governance.outbox_messages (
    id, organization_id, domain_event_id, destination, message_type, payload
)
VALUES (
    '01900000-0000-7000-8000-000000000061',
    '01900000-0000-7000-8000-000000000001',
    '01900000-0000-7000-8000-000000000051',
    'sync',
    'ValidationEvent',
    '{}'
);
SELECT count(*) FROM ops.claim_outbox(
    'validation-worker',
    '01900000-0000-7000-8000-000000000062',
    interval '1 minute',
    10
);
DO $$
BEGIN
    BEGIN
        PERFORM ops.complete_outbox(
            '01900000-0000-7000-8000-000000000061',
            '01900000-0000-7000-8000-000000000063'
        );
        RAISE EXCEPTION 'wrong outbox lock token was accepted';
    EXCEPTION
        WHEN serialization_failure THEN NULL;
    END;
END;
$$;
SELECT ops.complete_outbox(
    '01900000-0000-7000-8000-000000000061',
    '01900000-0000-7000-8000-000000000062'
);

SELECT pg_temp.assert_true(
    to_regclass('calendar.today_read_model') IS NOT NULL,
    'Today read model must exist'
);
SELECT pg_temp.assert_true(
    to_regclass('sync.snapshot_sessions') IS NOT NULL
    AND to_regclass('sync.snapshot_session_items') IS NOT NULL,
    'stable snapshot protocol tables must exist'
);
SELECT pg_temp.assert_true(
    to_regclass('work.recurrence_task_templates') IS NOT NULL,
    'recurrence task template must exist'
);
SELECT pg_temp.assert_true(
    to_regclass('files.file_location_device_states') IS NOT NULL,
    'file availability must be per-device'
);

SELECT pg_temp.assert_true(
    NOT EXISTS (
        SELECT 1
        FROM pg_constraint AS constraint_row
        JOIN pg_class AS child_class ON child_class.oid = constraint_row.conrelid
        JOIN pg_class AS parent_class ON parent_class.oid = constraint_row.confrelid
        JOIN pg_attribute AS child_attribute
          ON child_attribute.attrelid = constraint_row.conrelid
         AND child_attribute.attnum = constraint_row.conkey[1]
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
          AND NOT EXISTS (
              SELECT 1 FROM pg_trigger
              WHERE tgrelid = constraint_row.conrelid
                AND tgname LIKE 'trg_tenant_%'
                AND NOT tgisinternal
          )
    ),
    'all direct tenant references must have a database guard'
);

\echo 'DATABASE_CONTRACT_TESTS_PASSED'
