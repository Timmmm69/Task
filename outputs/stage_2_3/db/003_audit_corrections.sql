-- Organizer Stage 2.1 canonical organization bootstrap and authorization matrices.
-- Apply after 001_initial_schema.sql and 002_seed_authorization.sql.
BEGIN;

CREATE TABLE org.user_settings (
    user_account_id uuid PRIMARY KEY REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    language varchar(16) NOT NULL DEFAULT 'ru-RU',
    time_format varchar(4) NOT NULL DEFAULT '24h' CHECK (time_format IN ('12h','24h')),
    first_day_of_week smallint NOT NULL DEFAULT 1 CHECK (first_day_of_week BETWEEN 1 AND 7),
    workday_start time NOT NULL DEFAULT '09:00',
    workday_end time NOT NULL DEFAULT '18:00',
    weekend_days smallint[] NOT NULL DEFAULT ARRAY[6,7]::smallint[],
    default_task_duration_minutes integer NOT NULL DEFAULT 60 CHECK (default_task_duration_minutes BETWEEN 5 AND 1440),
    default_reminder_offset_minutes integer NOT NULL DEFAULT 15 CHECK (default_reminder_offset_minutes BETWEEN 0 AND 525600),
    autostart_enabled boolean NOT NULL DEFAULT true,
    allow_local_paths boolean NOT NULL DEFAULT true,
    confirm_catalog_delete boolean NOT NULL DEFAULT true,
    missing_file_behavior varchar(24) NOT NULL DEFAULT 'show_actions' CHECK (missing_file_behavior IN ('show_actions','keep_inactive','prompt_relink')),
    custom_preferences jsonb NOT NULL DEFAULT '{}'::jsonb,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_user_settings_org_user UNIQUE (organization_id, user_account_id),
    CONSTRAINT ck_user_settings_workday CHECK (workday_end > workday_start),
    CONSTRAINT ck_user_settings_weekend CHECK (
        cardinality(weekend_days) BETWEEN 1 AND 6
        AND weekend_days <@ ARRAY[1,2,3,4,5,6,7]::smallint[]
    )
);
CREATE INDEX ix_user_settings_org ON org.user_settings (organization_id, user_account_id);

CREATE OR REPLACE FUNCTION core.stable_seed_uuid(namespace_name text, seed_key text)
RETURNS uuid
LANGUAGE sql
IMMUTABLE
STRICT
PARALLEL SAFE
AS $$
    SELECT (
        substr(seed_hash, 1, 8) || '-' ||
        substr(seed_hash, 9, 4) || '-7' ||
        substr(seed_hash, 14, 3) || '-a' ||
        substr(seed_hash, 18, 3) || '-' ||
        substr(seed_hash, 21, 12)
    )::uuid
    FROM (SELECT md5(namespace_name || ':' || seed_key) AS seed_hash) AS value;
$$;

CREATE OR REPLACE FUNCTION iam.seed_organization_authorization(
    target_organization_id uuid,
    bootstrap_actor_id uuid
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, core, iam, org, projects
AS $$
DECLARE
    system_role_codes constant text[] := ARRAY['system_admin','manager','employee','observer'];
    project_role_codes constant text[] := ARRAY['project_owner','project_manager','project_editor','project_executor','project_observer'];
    manager_permissions constant text[] := ARRAY[
        'Archive.Restore','Calendar.Read','CalendarEvent.Create','CalendarEvent.Delete','CalendarEvent.Respond',
        'CalendarEvent.Update','Comment.Create','Comment.DeleteOwnOrModerate','Comment.Moderate','Comment.Read',
        'Comment.UpdateOwnOrModerate','Contact.Create','Contact.Delete','Contact.Read','Contact.Restore',
        'Contact.Update','Department.Manage','Department.Read','Device.ReadOwnOrAll','Device.Revoke',
        'Device.UpdateOwnOrAll','FileCatalog.Create','FileCatalog.Delete','FileCatalog.Read','FileCatalog.Restore',
        'FileCatalog.Update','FileLocation.Update','FileReference.Open','History.Read','Inbox.ManageOwn',
        'Inbox.ReadOwn','Interaction.Create','Interaction.Update','NetworkResource.Manage','Notification.ManageOwn',
        'Notification.ReadOwn','ObjectLink.Create','ObjectLink.Delete','ObjectLink.Read','Organization.Read',
        'Project.Archive','Project.Create','Project.Delete','Project.ManageMembers','Project.Read','Project.Restore',
        'Project.TransferOwnership','Project.Update','Reminder.ManageOwn','Role.Read','Search.Use','Session.ReadOwnOrAll',
        'Session.RevokeOwnOrAll','Settings.ReadOwn','Settings.UpdateOwn','Sync.Read','Tag.Assign','Tag.Manage','Tag.Read',
        'Task.Archive','Task.Assign','Task.ChangeStatus','Task.Create','Task.Delete','Task.ManageRecurrence',
        'Task.ManageWatchers','Task.Read','Task.Restore','Task.Update','Trash.Read','Trash.Restore','User.Read','User.Update'
    ];
    employee_permissions constant text[] := ARRAY[
        'Calendar.Read','CalendarEvent.Create','CalendarEvent.Respond','CalendarEvent.Update','Comment.Create',
        'Comment.DeleteOwnOrModerate','Comment.Read','Comment.UpdateOwnOrModerate','Contact.Create','Contact.Read',
        'Contact.Update','Department.Read','Device.ReadOwnOrAll','Device.UpdateOwnOrAll','FileCatalog.Create',
        'FileCatalog.Read','FileCatalog.Update','FileLocation.Update','FileReference.Open','History.Read',
        'Inbox.ManageOwn','Inbox.ReadOwn','Interaction.Create','Interaction.Update','Notification.ManageOwn',
        'Notification.ReadOwn','ObjectLink.Create','ObjectLink.Delete','ObjectLink.Read','Organization.Read',
        'Project.Create','Project.Read','Project.Update','Reminder.ManageOwn','Search.Use','Session.ReadOwnOrAll',
        'Session.RevokeOwnOrAll','Settings.ReadOwn','Settings.UpdateOwn','Sync.Read','Tag.Assign','Tag.Read',
        'Task.Assign','Task.ChangeStatus','Task.Create','Task.ManageRecurrence','Task.ManageWatchers','Task.Read',
        'Task.Update','User.Read'
    ];
    observer_permissions constant text[] := ARRAY[
        'Calendar.Read','Comment.Read','Contact.Read','Department.Read','FileCatalog.Read','FileReference.Open',
        'History.Read','Notification.ManageOwn','Notification.ReadOwn','ObjectLink.Read','Organization.Read',
        'Project.Read','Search.Use','Session.ReadOwnOrAll','Settings.ReadOwn','Sync.Read','Tag.Read','Task.Read','User.Read'
    ];
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('authorization:' || target_organization_id::text, 0));

    IF NOT EXISTS (SELECT 1 FROM core.organizations WHERE id = target_organization_id) THEN
        RAISE EXCEPTION 'ORGANIZATION_NOT_FOUND' USING ERRCODE = '23503';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM iam.user_accounts
        WHERE id = bootstrap_actor_id AND organization_id = target_organization_id
    ) THEN
        RAISE EXCEPTION 'BOOTSTRAP_ACTOR_SCOPE_MISMATCH' USING ERRCODE = '23514';
    END IF;

    INSERT INTO iam.roles (id, organization_id, code, name, scope_type, is_system, description)
    SELECT
        core.stable_seed_uuid('system-role', target_organization_id::text || ':' || role_code),
        target_organization_id,
        role_code,
        CASE role_code
            WHEN 'system_admin' THEN 'Системный администратор'
            WHEN 'manager' THEN 'Руководитель'
            WHEN 'employee' THEN 'Сотрудник'
            WHEN 'observer' THEN 'Наблюдатель'
        END,
        'organization',
        true,
        'Canonical Stage 2.1 system role'
    FROM unnest(system_role_codes) AS role_code
    ON CONFLICT (organization_id, code) DO UPDATE SET
        name = EXCLUDED.name,
        is_system = true,
        description = EXCLUDED.description;

    INSERT INTO projects.project_roles (id, organization_id, code, name, is_system, description)
    SELECT
        core.stable_seed_uuid('project-role', target_organization_id::text || ':' || role_code),
        target_organization_id,
        role_code,
        CASE role_code
            WHEN 'project_owner' THEN 'Владелец проекта'
            WHEN 'project_manager' THEN 'Менеджер проекта'
            WHEN 'project_editor' THEN 'Редактор проекта'
            WHEN 'project_executor' THEN 'Исполнитель проекта'
            WHEN 'project_observer' THEN 'Наблюдатель проекта'
        END,
        true,
        'Canonical Stage 2.1 project role'
    FROM unnest(project_role_codes) AS role_code
    ON CONFLICT (organization_id, code) DO UPDATE SET
        name = EXCLUDED.name,
        is_system = true,
        description = EXCLUDED.description;

    DELETE FROM iam.role_permissions
    WHERE role_id IN (SELECT id FROM iam.roles WHERE organization_id = target_organization_id AND is_system);

    INSERT INTO iam.role_permissions (role_id, permission_id, effect)
    SELECT role_row.id, permission_row.id, 'allow'
    FROM iam.roles AS role_row
    JOIN iam.permissions AS permission_row ON
        role_row.code = 'system_admin'
        OR (role_row.code = 'manager' AND permission_row.code::text = ANY(manager_permissions))
        OR (role_row.code = 'employee' AND permission_row.code::text = ANY(employee_permissions))
        OR (role_row.code = 'observer' AND permission_row.code::text = ANY(observer_permissions))
    WHERE role_row.organization_id = target_organization_id
      AND role_row.is_system;

    DELETE FROM projects.project_role_permissions
    WHERE project_role_id IN (
        SELECT id FROM projects.project_roles
        WHERE organization_id = target_organization_id AND is_system
    );

    INSERT INTO projects.project_role_permissions (project_role_id, permission_id, effect)
    SELECT project_role.id, permission_row.id, 'allow'
    FROM projects.project_roles AS project_role
    JOIN iam.permissions AS permission_row ON
        (project_role.code = 'project_owner' AND permission_row.code::text = ANY(ARRAY[
            'Project.Read','Project.Update','Project.Archive','Project.Delete','Project.Restore',
            'Project.ManageMembers','Project.TransferOwnership','Task.Read','Task.Create','Task.Update',
            'Task.ChangeStatus','Task.Assign','Task.Delete','Task.Restore','Task.Archive',
            'Task.ManageRecurrence','Task.ManageWatchers','Comment.Read','Comment.Create',
            'FileCatalog.Read','FileReference.Open','ObjectLink.Read','ObjectLink.Create','ObjectLink.Delete'
        ]::text[]))
        OR (project_role.code = 'project_manager' AND permission_row.code::text = ANY(ARRAY[
            'Project.Read','Project.Update','Project.Archive','Project.ManageMembers','Task.Read','Task.Create',
            'Task.Update','Task.ChangeStatus','Task.Assign','Task.Delete','Task.Restore','Task.Archive',
            'Task.ManageRecurrence','Task.ManageWatchers','Comment.Read','Comment.Create',
            'FileCatalog.Read','FileReference.Open','ObjectLink.Read','ObjectLink.Create','ObjectLink.Delete'
        ]::text[]))
        OR (project_role.code = 'project_editor' AND permission_row.code::text = ANY(ARRAY[
            'Project.Read','Project.Update','Task.Read','Task.Create','Task.Update','Task.ChangeStatus',
            'Task.Assign','Task.ManageWatchers','Comment.Read','Comment.Create','FileCatalog.Read',
            'FileReference.Open','ObjectLink.Read','ObjectLink.Create'
        ]::text[]))
        OR (project_role.code = 'project_executor' AND permission_row.code::text = ANY(ARRAY[
            'Project.Read','Task.Read','Task.ChangeStatus','Task.Update','Comment.Read','Comment.Create',
            'FileCatalog.Read','FileReference.Open','ObjectLink.Read'
        ]::text[]))
        OR (project_role.code = 'project_observer' AND permission_row.code::text = ANY(ARRAY[
            'Project.Read','Task.Read','Comment.Read','FileCatalog.Read','FileReference.Open','ObjectLink.Read'
        ]::text[]))
    WHERE project_role.organization_id = target_organization_id
      AND project_role.is_system;

    INSERT INTO iam.user_roles (
        id, organization_id, user_account_id, role_id, granted_by
    )
    SELECT
        core.stable_seed_uuid('user-role', target_organization_id::text || ':' || bootstrap_actor_id::text || ':system_admin'),
        target_organization_id,
        bootstrap_actor_id,
        role_row.id,
        bootstrap_actor_id
    FROM iam.roles AS role_row
    WHERE role_row.organization_id = target_organization_id
      AND role_row.code = 'system_admin'
    ON CONFLICT (user_account_id, role_id, department_id) DO NOTHING;
END;
$$;

CREATE OR REPLACE FUNCTION iam.bootstrap_first_administrator(
    target_organization_id uuid,
    organization_code text,
    organization_name text,
    organization_time_zone text,
    target_profile_id uuid,
    target_user_id uuid,
    normalized_login text,
    argon2id_password_hash text,
    first_name text,
    last_name text
)
RETURNS void
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, core, iam, org, projects
AS $$
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended('bootstrap:' || target_organization_id::text, 0));

    IF argon2id_password_hash NOT LIKE '$argon2id$%' THEN
        RAISE EXCEPTION 'BOOTSTRAP_PASSWORD_MUST_BE_ARGON2ID' USING ERRCODE = '22023';
    END IF;
    IF EXISTS (
        SELECT 1 FROM iam.user_accounts
        WHERE organization_id = target_organization_id AND id <> target_user_id
    ) THEN
        RAISE EXCEPTION 'ORGANIZATION_ALREADY_BOOTSTRAPPED' USING ERRCODE = '23505';
    END IF;

    INSERT INTO core.organizations (id, code, name, default_time_zone)
    VALUES (target_organization_id, organization_code, organization_name, organization_time_zone)
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO core.organization_settings (organization_id)
    VALUES (target_organization_id)
    ON CONFLICT (organization_id) DO NOTHING;

    INSERT INTO core.objects (id, organization_id, object_type)
    VALUES
        (target_profile_id, target_organization_id, 'employee_profile'),
        (target_user_id, target_organization_id, 'user_account')
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO org.employee_profiles (
        id, organization_id, first_name, last_name, display_name, employment_status
    )
    VALUES (
        target_profile_id,
        target_organization_id,
        first_name,
        last_name,
        btrim(first_name || ' ' || last_name),
        'active'
    )
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO iam.user_accounts (
        id, organization_id, employee_profile_id, login, password_hash,
        account_status, must_change_password
    )
    VALUES (
        target_user_id, target_organization_id, target_profile_id, normalized_login,
        argon2id_password_hash, 'active', true
    )
    ON CONFLICT (id) DO NOTHING;

    UPDATE core.objects
    SET created_by = target_user_id,
        updated_by = target_user_id
    WHERE organization_id = target_organization_id
      AND id IN (target_profile_id, target_user_id)
      AND created_by IS NULL;

    INSERT INTO org.user_settings (user_account_id, organization_id)
    VALUES (target_user_id, target_organization_id)
    ON CONFLICT (user_account_id) DO NOTHING;

    INSERT INTO iam.authorization_scope_versions (organization_id, user_account_id)
    VALUES (target_organization_id, target_user_id)
    ON CONFLICT (organization_id, user_account_id) DO NOTHING;

    PERFORM iam.seed_organization_authorization(target_organization_id, target_user_id);
END;
$$;

COMMENT ON FUNCTION iam.bootstrap_first_administrator(
    uuid, text, text, text, uuid, uuid, text, text, text, text
) IS 'Single-transaction first-organization bootstrap. Deployment supplies UUIDv7 values and a precomputed Argon2id hash.';

COMMIT;
