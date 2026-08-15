CREATE EXTENSION IF NOT EXISTS citext;

CREATE SCHEMA IF NOT EXISTS core;
CREATE SCHEMA IF NOT EXISTS work;

CREATE TABLE core.organizations (
    id uuid PRIMARY KEY,
    code citext NOT NULL,
    name text NOT NULL,
    default_time_zone text NOT NULL,
    locale varchar(16) NOT NULL DEFAULT 'ru-RU',
    status varchar(20) NOT NULL DEFAULT 'active',
    version bigint NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    archived_at timestamptz,
    deleted_at timestamptz,
    CONSTRAINT uq_organizations_code UNIQUE (code),
    CONSTRAINT ck_organizations_status CHECK (status IN ('active', 'suspended', 'closed')),
    CONSTRAINT ck_organizations_version CHECK (version > 0),
    CONSTRAINT ck_organizations_name CHECK (length(btrim(name)) BETWEEN 1 AND 200),
    CONSTRAINT ck_organizations_timezone CHECK (length(default_time_zone) BETWEEN 1 AND 64)
);

CREATE TABLE core.objects (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    object_type varchar(40) NOT NULL,
    lifecycle_state varchar(20) NOT NULL DEFAULT 'active',
    lifecycle_state_before_trash varchar(20),
    version bigint NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL,
    created_by uuid NOT NULL,
    updated_at timestamptz NOT NULL,
    updated_by uuid NOT NULL,
    archived_at timestamptz,
    deleted_at timestamptz,
    deleted_by uuid,
    CONSTRAINT uq_objects_org_id UNIQUE (organization_id, id),
    CONSTRAINT ck_objects_type CHECK (object_type IN (
        'user_account', 'employee_profile', 'department', 'device', 'project', 'inbox_item', 'task',
        'calendar_event', 'catalog_item', 'network_resource', 'contact', 'company', 'interaction',
        'notification', 'tag', 'system_asset'
    )),
    CONSTRAINT ck_objects_version CHECK (version > 0),
    CONSTRAINT ck_objects_lifecycle CHECK (lifecycle_state IN ('active', 'archived', 'trashed')),
    CONSTRAINT ck_objects_previous_lifecycle CHECK (
        lifecycle_state_before_trash IS NULL OR lifecycle_state_before_trash IN ('active', 'archived')
    ),
    CONSTRAINT ck_objects_timestamps CHECK (
        updated_at >= created_at AND
        (archived_at IS NULL OR archived_at BETWEEN created_at AND updated_at) AND
        (deleted_at IS NULL OR deleted_at BETWEEN created_at AND updated_at)
    ),
    CONSTRAINT ck_objects_lifecycle_fields CHECK (
        (lifecycle_state = 'active' AND lifecycle_state_before_trash IS NULL AND
            archived_at IS NULL AND deleted_at IS NULL AND deleted_by IS NULL)
        OR
        (lifecycle_state = 'archived' AND lifecycle_state_before_trash IS NULL AND
            archived_at IS NOT NULL AND deleted_at IS NULL AND deleted_by IS NULL)
        OR
        (lifecycle_state = 'trashed' AND lifecycle_state_before_trash IS NOT NULL AND
            deleted_at IS NOT NULL AND deleted_by IS NOT NULL AND
            ((lifecycle_state_before_trash = 'active' AND archived_at IS NULL) OR
             (lifecycle_state_before_trash = 'archived' AND archived_at IS NOT NULL)))
    )
);

CREATE INDEX ix_objects_org_type_state
    ON core.objects (organization_id, object_type, lifecycle_state, updated_at DESC);
CREATE INDEX ix_objects_trash_deleted
    ON core.objects (organization_id, deleted_at)
    WHERE lifecycle_state = 'trashed';

CREATE TABLE work.tasks (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL,
    title text NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'new',
    priority varchar(16) NOT NULL DEFAULT 'normal',
    start_at_utc timestamptz,
    deadline_at timestamptz,
    completed_at timestamptz,
    completed_by uuid,
    CONSTRAINT uq_tasks_org_id UNIQUE (organization_id, id),
    CONSTRAINT fk_tasks_object_org FOREIGN KEY (organization_id, id)
        REFERENCES core.objects(organization_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_tasks_title CHECK (length(btrim(title)) BETWEEN 1 AND 500),
    CONSTRAINT ck_tasks_status CHECK (status IN ('new', 'in_progress', 'review', 'completed', 'cancelled')),
    CONSTRAINT ck_tasks_priority CHECK (priority IN ('low', 'normal', 'high', 'critical')),
    CONSTRAINT ck_tasks_schedule CHECK (
        deadline_at IS NULL OR start_at_utc IS NULL OR deadline_at >= start_at_utc
    ),
    CONSTRAINT ck_tasks_completion CHECK (
        (status = 'completed' AND completed_at IS NOT NULL AND completed_by IS NOT NULL)
        OR
        (status <> 'completed' AND completed_at IS NULL AND completed_by IS NULL)
    )
);

CREATE INDEX ix_tasks_org_status_deadline
    ON work.tasks (organization_id, status, deadline_at);
CREATE INDEX ix_tasks_org_schedule
    ON work.tasks (organization_id, start_at_utc)
    WHERE start_at_utc IS NOT NULL;
CREATE INDEX ix_tasks_org_deadline_open
    ON work.tasks (organization_id, deadline_at, priority)
    WHERE status NOT IN ('completed', 'cancelled') AND deadline_at IS NOT NULL;
