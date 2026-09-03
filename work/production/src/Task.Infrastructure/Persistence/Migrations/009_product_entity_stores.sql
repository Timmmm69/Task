CREATE SCHEMA IF NOT EXISTS projects;
CREATE SCHEMA IF NOT EXISTS files;
CREATE SCHEMA IF NOT EXISTS crm;
CREATE SCHEMA IF NOT EXISTS notify;

CREATE TABLE core.organization_settings (
    organization_id uuid PRIMARY KEY REFERENCES core.organizations(id) ON DELETE CASCADE,
    trash_retention_days integer NOT NULL DEFAULT 30 CHECK (trash_retention_days BETWEEN 1 AND 3650),
    history_retention_days integer NOT NULL DEFAULT 1095 CHECK (history_retention_days BETWEEN 90 AND 36500),
    change_feed_retention_days integer NOT NULL DEFAULT 90 CHECK (change_feed_retention_days BETWEEN 7 AND 3650),
    recurrence_horizon_days integer NOT NULL DEFAULT 90 CHECK (recurrence_horizon_days BETWEEN 7 AND 730),
    recurrence_min_instances integer NOT NULL DEFAULT 20 CHECK (recurrence_min_instances BETWEEN 1 AND 500),
    default_workday_start time NOT NULL DEFAULT '09:00',
    default_workday_end time NOT NULL DEFAULT '18:00',
    first_day_of_week smallint NOT NULL DEFAULT 1 CHECK (first_day_of_week BETWEEN 1 AND 7),
    max_request_bytes integer NOT NULL DEFAULT 1048576 CHECK (max_request_bytes BETWEEN 65536 AND 10485760),
    updated_at timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    CONSTRAINT ck_organization_settings_workday CHECK (default_workday_end > default_workday_start)
);

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
    missing_file_behavior varchar(24) NOT NULL DEFAULT 'show_actions'
        CHECK (missing_file_behavior IN ('show_actions','keep_inactive','prompt_relink')),
    custom_preferences jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(custom_preferences) = 'object'),
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    updated_at timestamptz NOT NULL,
    CONSTRAINT uq_user_settings_org_user UNIQUE (organization_id, user_account_id),
    CONSTRAINT fk_user_settings_user_org FOREIGN KEY (organization_id, user_account_id)
        REFERENCES iam.user_accounts(organization_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_user_settings_workday CHECK (workday_end > workday_start),
    CONSTRAINT ck_user_settings_weekend CHECK (
        cardinality(weekend_days) BETWEEN 1 AND 6
        AND weekend_days <@ ARRAY[1,2,3,4,5,6,7]::smallint[]
    )
);

CREATE TABLE projects.projects (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    name text NOT NULL,
    description text,
    owner_user_id uuid NOT NULL,
    manager_user_id uuid,
    status varchar(20) NOT NULL DEFAULT 'planning'
        CHECK (status IN ('planning','active','paused','completed')),
    start_date date,
    planned_end_date date,
    actual_end_at timestamptz,
    default_time_zone text,
    color_code varchar(9),
    CONSTRAINT uq_projects_org_id UNIQUE (organization_id, id),
    CONSTRAINT fk_projects_object_org FOREIGN KEY (organization_id, id)
        REFERENCES core.objects(organization_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_projects_owner_org FOREIGN KEY (organization_id, owner_user_id)
        REFERENCES iam.user_accounts(organization_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_projects_manager_org FOREIGN KEY (organization_id, manager_user_id)
        REFERENCES iam.user_accounts(organization_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_projects_name CHECK (length(btrim(name)) BETWEEN 1 AND 300),
    CONSTRAINT ck_projects_description CHECK (description IS NULL OR length(description) <= 20000),
    CONSTRAINT ck_projects_dates CHECK (
        planned_end_date IS NULL OR start_date IS NULL OR planned_end_date >= start_date
    ),
    CONSTRAINT ck_projects_actual_end CHECK (status <> 'completed' OR actual_end_at IS NOT NULL),
    CONSTRAINT ck_projects_timezone CHECK (default_time_zone IS NULL OR length(default_time_zone) BETWEEN 1 AND 64),
    CONSTRAINT ck_projects_color CHECK (color_code IS NULL OR color_code ~ '^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$')
);

CREATE INDEX ix_projects_org_status ON projects.projects (organization_id, status, name);

CREATE TABLE crm.contacts (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    first_name text NOT NULL,
    last_name text,
    middle_name text,
    display_name text NOT NULL,
    notes text,
    status varchar(16) NOT NULL DEFAULT 'active' CHECK (status IN ('active','inactive')),
    CONSTRAINT uq_contacts_org_id UNIQUE (organization_id, id),
    CONSTRAINT fk_contacts_object_org FOREIGN KEY (organization_id, id)
        REFERENCES core.objects(organization_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_contacts_first_name CHECK (length(btrim(first_name)) BETWEEN 1 AND 100),
    CONSTRAINT ck_contacts_last_name CHECK (last_name IS NULL OR length(last_name) <= 100),
    CONSTRAINT ck_contacts_middle_name CHECK (middle_name IS NULL OR length(middle_name) <= 100),
    CONSTRAINT ck_contacts_display_name CHECK (length(btrim(display_name)) BETWEEN 1 AND 300),
    CONSTRAINT ck_contacts_notes CHECK (notes IS NULL OR length(notes) <= 20000)
);

CREATE INDEX ix_contacts_org_status_name ON crm.contacts (organization_id, status, display_name);

CREATE TABLE files.catalog_items (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    parent_item_id uuid,
    item_type varchar(24) NOT NULL
        CHECK (item_type IN ('virtual_folder','file_reference','folder_reference','web_link','text_note')),
    name text NOT NULL,
    description text,
    note_content text,
    web_url text,
    mime_type varchar(200),
    file_extension varchar(32),
    observed_size_bytes bigint CHECK (observed_size_bytes IS NULL OR observed_size_bytes >= 0),
    observed_modified_at timestamptz,
    sort_order integer NOT NULL DEFAULT 0,
    created_by uuid NOT NULL,
    CONSTRAINT uq_catalog_items_org_id UNIQUE (organization_id, id),
    CONSTRAINT fk_catalog_items_object_org FOREIGN KEY (organization_id, id)
        REFERENCES core.objects(organization_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_catalog_items_parent_org FOREIGN KEY (organization_id, parent_item_id)
        REFERENCES files.catalog_items(organization_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_catalog_items_creator_org FOREIGN KEY (organization_id, created_by)
        REFERENCES iam.user_accounts(organization_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_catalog_items_name CHECK (length(btrim(name)) BETWEEN 1 AND 500),
    CONSTRAINT ck_catalog_items_description CHECK (description IS NULL OR length(description) <= 20000),
    CONSTRAINT ck_catalog_items_note CHECK (note_content IS NULL OR length(note_content) <= 100000),
    CONSTRAINT ck_catalog_items_web_url CHECK (web_url IS NULL OR length(web_url) <= 2048),
    CONSTRAINT ck_catalog_items_content CHECK (
        (item_type = 'web_link' AND web_url IS NOT NULL AND note_content IS NULL) OR
        (item_type = 'text_note' AND note_content IS NOT NULL AND web_url IS NULL) OR
        (item_type NOT IN ('web_link','text_note') AND web_url IS NULL AND note_content IS NULL)
    ),
    CONSTRAINT ck_catalog_items_parent_not_self CHECK (parent_item_id IS NULL OR parent_item_id <> id)
);

CREATE INDEX ix_catalog_items_org_parent_order
    ON files.catalog_items (organization_id, parent_item_id, sort_order, name);

CREATE TABLE notify.notifications (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    recipient_user_id uuid NOT NULL,
    notification_type varchar(40) NOT NULL,
    source_object_id uuid,
    title text NOT NULL,
    body text NOT NULL,
    severity varchar(12) NOT NULL DEFAULT 'info' CHECK (severity IN ('info','warning','critical')),
    status varchar(16) NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending','delivered','read','dismissed','failed','expired')),
    not_before timestamptz NOT NULL,
    expires_at timestamptz,
    delivered_at timestamptz,
    read_at timestamptz,
    dismissed_at timestamptz,
    deduplication_key varchar(200),
    action_payload jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (jsonb_typeof(action_payload) = 'object'),
    CONSTRAINT uq_notifications_org_id UNIQUE (organization_id, id),
    CONSTRAINT fk_notifications_object_org FOREIGN KEY (organization_id, id)
        REFERENCES core.objects(organization_id, id) ON DELETE RESTRICT,
    CONSTRAINT fk_notifications_recipient_org FOREIGN KEY (organization_id, recipient_user_id)
        REFERENCES iam.user_accounts(organization_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_notifications_source_org FOREIGN KEY (organization_id, source_object_id)
        REFERENCES core.objects(organization_id, id) ON DELETE SET NULL (source_object_id),
    CONSTRAINT ck_notifications_type CHECK (length(btrim(notification_type)) BETWEEN 1 AND 40),
    CONSTRAINT ck_notifications_title CHECK (length(btrim(title)) BETWEEN 1 AND 500),
    CONSTRAINT ck_notifications_body CHECK (length(body) BETWEEN 1 AND 10000),
    CONSTRAINT ck_notifications_expiry CHECK (expires_at IS NULL OR expires_at > not_before),
    CONSTRAINT ck_notifications_status_times CHECK (
        (status NOT IN ('delivered','read') OR delivered_at IS NOT NULL) AND
        (status <> 'read' OR read_at IS NOT NULL) AND
        (status <> 'dismissed' OR dismissed_at IS NOT NULL)
    )
);

CREATE UNIQUE INDEX uq_notifications_org_dedup
    ON notify.notifications (organization_id, recipient_user_id, deduplication_key)
    WHERE deduplication_key IS NOT NULL;
CREATE INDEX ix_notifications_recipient_status
    ON notify.notifications (organization_id, recipient_user_id, status, not_before DESC);

CREATE TABLE notify.notification_preferences (
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL,
    notification_type varchar(40) NOT NULL,
    enabled boolean NOT NULL DEFAULT true,
    desktop_enabled boolean NOT NULL DEFAULT true,
    sound_enabled boolean NOT NULL DEFAULT true,
    default_snooze_minutes integer NOT NULL DEFAULT 15 CHECK (default_snooze_minutes BETWEEN 1 AND 10080),
    quiet_hours_start time,
    quiet_hours_end time,
    quiet_hours_time_zone text,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    updated_at timestamptz NOT NULL,
    PRIMARY KEY (organization_id, user_account_id, notification_type),
    CONSTRAINT fk_notification_preferences_user_org FOREIGN KEY (organization_id, user_account_id)
        REFERENCES iam.user_accounts(organization_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_notification_preferences_type CHECK (length(btrim(notification_type)) BETWEEN 1 AND 40),
    CONSTRAINT ck_notification_preferences_quiet_hours CHECK (
        (quiet_hours_start IS NULL AND quiet_hours_end IS NULL AND quiet_hours_time_zone IS NULL) OR
        (quiet_hours_start IS NOT NULL AND quiet_hours_end IS NOT NULL AND quiet_hours_time_zone IS NOT NULL)
    )
);

CREATE TABLE governance.archive_entries (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    object_id uuid NOT NULL,
    object_type varchar(40) NOT NULL,
    archived_by uuid NOT NULL,
    archived_at timestamptz NOT NULL,
    reason text,
    status varchar(16) NOT NULL DEFAULT 'archived' CHECK (status IN ('archived','restored')),
    restored_by uuid,
    restored_at timestamptz,
    CONSTRAINT fk_archive_object_org FOREIGN KEY (organization_id, object_id)
        REFERENCES core.objects(organization_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_archive_reason CHECK (reason IS NULL OR length(reason) <= 2000),
    CONSTRAINT ck_archive_status_time CHECK (
        (status = 'archived' AND restored_by IS NULL AND restored_at IS NULL) OR
        (status = 'restored' AND restored_by IS NOT NULL AND restored_at IS NOT NULL)
    )
);

CREATE UNIQUE INDEX uq_archive_entries_current
    ON governance.archive_entries (organization_id, object_id) WHERE status = 'archived';

CREATE TABLE governance.trash_entries (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    object_id uuid NOT NULL,
    object_type varchar(40) NOT NULL,
    deleted_by uuid NOT NULL,
    deleted_at timestamptz NOT NULL,
    purge_after timestamptz NOT NULL,
    deletion_reason text,
    status varchar(20) NOT NULL DEFAULT 'retained'
        CHECK (status IN ('retained','restored','purged','blocked_by_hold')),
    restored_by uuid,
    restored_at timestamptz,
    purged_at timestamptz,
    CONSTRAINT fk_trash_object_org FOREIGN KEY (organization_id, object_id)
        REFERENCES core.objects(organization_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_trash_purge_after CHECK (purge_after > deleted_at),
    CONSTRAINT ck_trash_reason CHECK (deletion_reason IS NULL OR length(deletion_reason) <= 2000),
    CONSTRAINT ck_trash_status_time CHECK (
        (status IN ('retained','blocked_by_hold') AND restored_by IS NULL AND restored_at IS NULL AND purged_at IS NULL) OR
        (status = 'restored' AND restored_by IS NOT NULL AND restored_at IS NOT NULL AND purged_at IS NULL) OR
        (status = 'purged' AND restored_by IS NULL AND restored_at IS NULL AND purged_at IS NOT NULL)
    )
);

CREATE UNIQUE INDEX uq_trash_entries_current
    ON governance.trash_entries (organization_id, object_id)
    WHERE status IN ('retained','blocked_by_hold');

-- The ledger follows the shared object lifecycle, including the already shipped task/calendar stores.
-- Actor identifiers retain core.objects semantics; authentication/authorization owns actor validation.
INSERT INTO governance.archive_entries (
    id, organization_id, object_id, object_type, archived_by, archived_at)
SELECT gen_random_uuid(), organization_id, id, object_type, updated_by, archived_at
FROM core.objects WHERE archived_at IS NOT NULL;

INSERT INTO governance.trash_entries (
    id, organization_id, object_id, object_type, deleted_by, deleted_at, purge_after)
SELECT gen_random_uuid(), o.organization_id, o.id, o.object_type, o.deleted_by, o.deleted_at,
    o.deleted_at + make_interval(days => COALESCE(s.trash_retention_days, 30))
FROM core.objects o LEFT JOIN core.organization_settings s ON s.organization_id = o.organization_id
WHERE o.lifecycle_state = 'trashed';

CREATE FUNCTION governance.record_product_lifecycle() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.lifecycle_state = NEW.lifecycle_state THEN
        RETURN NEW;
    END IF;

    IF OLD.lifecycle_state = 'active' AND NEW.lifecycle_state = 'archived' THEN
        INSERT INTO governance.archive_entries (
            id, organization_id, object_id, object_type, archived_by, archived_at)
        VALUES (gen_random_uuid(), NEW.organization_id, NEW.id, NEW.object_type, NEW.updated_by, NEW.archived_at);
    ELSIF OLD.lifecycle_state = 'archived' AND NEW.lifecycle_state = 'active' THEN
        UPDATE governance.archive_entries SET status = 'restored', restored_by = NEW.updated_by,
            restored_at = NEW.updated_at
        WHERE organization_id = NEW.organization_id AND object_id = NEW.id AND status = 'archived';
    END IF;

    IF NEW.lifecycle_state = 'trashed' THEN
        INSERT INTO governance.trash_entries (
            id, organization_id, object_id, object_type, deleted_by, deleted_at, purge_after)
        VALUES (gen_random_uuid(), NEW.organization_id, NEW.id, NEW.object_type, NEW.deleted_by, NEW.deleted_at,
            NEW.deleted_at + make_interval(days => COALESCE(
                (SELECT trash_retention_days FROM core.organization_settings WHERE organization_id = NEW.organization_id), 30)));
    ELSIF OLD.lifecycle_state = 'trashed' THEN
        UPDATE governance.trash_entries SET status = 'restored', restored_by = NEW.updated_by,
            restored_at = NEW.updated_at
        WHERE organization_id = NEW.organization_id AND object_id = NEW.id AND status IN ('retained','blocked_by_hold');
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_record_product_lifecycle
AFTER UPDATE OF lifecycle_state ON core.objects
FOR EACH ROW EXECUTE FUNCTION governance.record_product_lifecycle();
