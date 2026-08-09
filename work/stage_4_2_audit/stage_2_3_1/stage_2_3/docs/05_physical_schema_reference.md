# Этап 2. Детальная модель данных, PostgreSQL, API, права и технические сценарии

> Этот том фиксирует baseline `001`/`003`. Нормативное итоговое состояние Этапа 2.1 получается последовательным применением `db/001...004`; новые таблицы, ограничения, triggers и runtime roles определены в `004_stage_2_1_foundation.sql` и проверяются по live PostgreSQL catalog.

**Продукт:** десктопный органайзер для одной компании  
**Статус:** нормативная техническая спецификация перед реализацией  
**Архитектурная база:** Этап 1, версия 1.0  
**Целевая БД:** PostgreSQL 16+  
**API:** REST `/api/v1`, OpenAPI 3.1.0  
**Идентификаторы:** UUIDv7, генерируются приложением  
**Конкурентность:** optimistic locking через ETag/If-Match  
**Синхронизация:** bootstrap + change feed + WebSocket invalidation  

> Нормативный приоритет: концепция определяет бизнес-функции; Этап 1 определяет архитектуру; данный пакет конкретизирует реализацию. При расхождении действует явно зафиксированное решение раздела 1.

# 6A. Полный справочник физической схемы

Каждый блок ниже является нормативным DDL. Индексы, относящиеся к таблице, перечислены после блока.

## 6A.1. `core.organizations`

**Назначение:** Единственная организация/tenant.

```sql
CREATE TABLE core.organizations (
    id uuid PRIMARY KEY,
    code citext NOT NULL,
    name text NOT NULL,
    default_time_zone text NOT NULL,
    locale varchar(16) NOT NULL DEFAULT 'ru-RU',
    status varchar(20) NOT NULL DEFAULT 'active' CHECK (status IN ('active','suspended','closed')),
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    archived_at timestamptz,
    deleted_at timestamptz,
    CONSTRAINT uq_organizations_code UNIQUE (code),
    CONSTRAINT ck_organizations_name CHECK (length(btrim(name)) BETWEEN 1 AND 200),
    CONSTRAINT ck_organizations_timezone CHECK (length(default_time_zone) BETWEEN 1 AND 64)
);
```

## 6A.2. `core.objects`

**Назначение:** Общий реестр lifecycle/version/audit identity.

```sql
CREATE TABLE core.objects (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    object_type varchar(40) NOT NULL CHECK (object_type IN (
        'user_account','employee_profile','department','device','project','inbox_item','task',
        'calendar_event','catalog_item','network_resource','contact','company','interaction',
        'notification','tag','system_asset'
    )),
    lifecycle_state varchar(20) NOT NULL DEFAULT 'active' CHECK (lifecycle_state IN ('active','archived','trashed','purged')),
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    created_by uuid,
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_by uuid,
    archived_at timestamptz,
    archived_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,
    purge_after timestamptz,
    legal_hold boolean NOT NULL DEFAULT false,
    CONSTRAINT uq_objects_org_id UNIQUE (organization_id, id),
    CONSTRAINT ck_objects_lifecycle_dates CHECK (
        (lifecycle_state <> 'archived' OR archived_at IS NOT NULL) AND
        (lifecycle_state NOT IN ('trashed','purged') OR deleted_at IS NOT NULL)
    )
);
```

**Индексы:**

- `CREATE INDEX ix_objects_org_type_state ON core.objects (organization_id, object_type, lifecycle_state, updated_at DESC);`
- `CREATE INDEX ix_objects_trash_purge ON core.objects (organization_id, purge_after) WHERE lifecycle_state = 'trashed' AND legal_hold = false;`

## 6A.3. `core.organization_settings`

**Назначение:** Организационные retention и системные defaults.

```sql
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
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0)
);
```

## 6A.4. `org.departments`

**Назначение:** Иерархия отделов.

```sql
CREATE TABLE org.departments (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    code citext NOT NULL,
    name text NOT NULL,
    description text,
    parent_department_id uuid REFERENCES org.departments(id) ON DELETE RESTRICT,
    sort_order integer NOT NULL DEFAULT 0,
    CONSTRAINT fk_departments_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT uq_departments_org_code UNIQUE (organization_id, code),
    CONSTRAINT ck_departments_name CHECK (length(btrim(name)) BETWEEN 1 AND 200),
    CONSTRAINT ck_departments_not_self CHECK (parent_department_id IS NULL OR parent_department_id <> id)
);
```

**Индексы:**

- `CREATE INDEX ix_departments_org_parent ON org.departments (organization_id, parent_department_id, sort_order, name);`

## 6A.5. `org.employee_profiles`

**Назначение:** Рабочие профили сотрудников.

```sql
CREATE TABLE org.employee_profiles (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    department_id uuid REFERENCES org.departments(id) ON DELETE SET NULL,
    first_name text NOT NULL,
    last_name text NOT NULL,
    middle_name text,
    display_name text NOT NULL,
    job_title text,
    work_email citext,
    internal_phone text,
    avatar_asset_id uuid,
    employment_status varchar(20) NOT NULL DEFAULT 'active' CHECK (employment_status IN ('pre_hire','active','leave','terminated')),
    hired_on date,
    terminated_on date,
    preferred_time_zone text,
    locale varchar(16) NOT NULL DEFAULT 'ru-RU',
    time_format varchar(4) NOT NULL DEFAULT '24h' CHECK (time_format IN ('12h','24h')),
    CONSTRAINT fk_employee_profiles_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT ck_employee_profiles_names CHECK (length(btrim(first_name)) BETWEEN 1 AND 100 AND length(btrim(last_name)) BETWEEN 1 AND 100),
    CONSTRAINT ck_employee_profiles_dates CHECK (terminated_on IS NULL OR hired_on IS NULL OR terminated_on >= hired_on)
);
```

**Индексы:**

- `CREATE INDEX ix_employee_profiles_org_department ON org.employee_profiles (organization_id, department_id, employment_status, display_name);`
- `CREATE UNIQUE INDEX uq_employee_profiles_work_email ON org.employee_profiles (organization_id, work_email) WHERE work_email IS NOT NULL;`
- `CREATE INDEX ix_employee_profiles_display_trgm ON org.employee_profiles USING gin (display_name gin_trgm_ops);`

## 6A.6. `iam.user_accounts`

**Назначение:** Учётные записи и credential state.

```sql
CREATE TABLE iam.user_accounts (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    employee_profile_id uuid NOT NULL REFERENCES org.employee_profiles(id) ON DELETE RESTRICT,
    login citext NOT NULL,
    password_hash text NOT NULL,
    password_algorithm varchar(20) NOT NULL DEFAULT 'argon2id',
    password_parameters jsonb NOT NULL DEFAULT '{}'::jsonb,
    credential_version integer NOT NULL DEFAULT 1 CHECK (credential_version > 0),
    account_status varchar(24) NOT NULL DEFAULT 'pending_activation' CHECK (account_status IN ('pending_activation','active','blocked','deactivated')),
    must_change_password boolean NOT NULL DEFAULT true,
    failed_login_count integer NOT NULL DEFAULT 0 CHECK (failed_login_count >= 0),
    locked_until timestamptz,
    last_login_at timestamptz,
    last_activity_at timestamptz,
    CONSTRAINT fk_user_accounts_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT uq_user_accounts_org_profile UNIQUE (organization_id, employee_profile_id),
    CONSTRAINT uq_user_accounts_org_login UNIQUE (organization_id, login),
    CONSTRAINT ck_user_accounts_login CHECK (length(login::text) BETWEEN 3 AND 100)
);
```

**Индексы:**

- `CREATE INDEX ix_user_accounts_org_status ON iam.user_accounts (organization_id, account_status, last_activity_at DESC);`

## 6A.7. `core.system_assets`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE core.system_assets (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    asset_type varchar(20) NOT NULL CHECK (asset_type IN ('avatar','logo')),
    media_type varchar(100) NOT NULL CHECK (media_type IN ('image/png','image/jpeg','image/webp')),
    byte_length integer NOT NULL CHECK (byte_length BETWEEN 1 AND 2097152),
    sha256 bytea NOT NULL CHECK (octet_length(sha256) = 32),
    storage_key text NOT NULL,
    created_by uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT fk_system_assets_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT uq_system_assets_storage_key UNIQUE (organization_id, storage_key)
);
```

## 6A.8. `iam.password_history`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE iam.password_history (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    password_hash text NOT NULL,
    password_algorithm varchar(20) NOT NULL,
    password_parameters jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
```

**Индексы:**

- `CREATE INDEX ix_password_history_user_created ON iam.password_history (user_account_id, created_at DESC);`

## 6A.9. `iam.devices`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE iam.devices (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    device_key_hash bytea NOT NULL,
    device_name text NOT NULL,
    os_version text,
    app_version varchar(32),
    status varchar(20) NOT NULL DEFAULT 'active' CHECK (status IN ('active','revoked','retired')),
    first_seen_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    last_seen_at timestamptz,
    last_ip inet,
    CONSTRAINT fk_devices_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT uq_devices_org_key UNIQUE (organization_id, device_key_hash),
    CONSTRAINT ck_devices_name CHECK (length(btrim(device_name)) BETWEEN 1 AND 200)
);
```

**Индексы:**

- `CREATE INDEX ix_devices_org_status_seen ON iam.devices (organization_id, status, last_seen_at DESC);`

## 6A.10. `iam.sessions`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE iam.sessions (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    device_id uuid NOT NULL REFERENCES iam.devices(id) ON DELETE RESTRICT,
    token_family_id uuid NOT NULL,
    status varchar(24) NOT NULL DEFAULT 'active' CHECK (status IN ('active','revoked','idle_expired','absolute_expired','compromised')),
    credential_version integer NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    last_seen_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    idle_expires_at timestamptz NOT NULL,
    absolute_expires_at timestamptz NOT NULL,
    revoked_at timestamptz,
    revoked_by uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    revoke_reason varchar(100),
    created_ip inet,
    last_ip inet,
    user_agent text,
    CONSTRAINT ck_sessions_expiry CHECK (idle_expires_at <= absolute_expires_at),
    CONSTRAINT ck_sessions_revoked CHECK ((status = 'active' AND revoked_at IS NULL) OR status <> 'active')
);
```

**Индексы:**

- `CREATE INDEX ix_sessions_user_active ON iam.sessions (organization_id, user_account_id, status, last_seen_at DESC);`
- `CREATE INDEX ix_sessions_device_active ON iam.sessions (device_id, status, last_seen_at DESC);`
- `CREATE INDEX ix_sessions_expiry ON iam.sessions (status, idle_expires_at, absolute_expires_at) WHERE status = 'active';`

## 6A.11. `iam.refresh_tokens`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE iam.refresh_tokens (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    session_id uuid NOT NULL REFERENCES iam.sessions(id) ON DELETE CASCADE,
    token_hash bytea NOT NULL CHECK (octet_length(token_hash) = 32),
    generation integer NOT NULL CHECK (generation > 0),
    issued_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz,
    replaced_by_token_id uuid REFERENCES iam.refresh_tokens(id) ON DELETE SET NULL,
    revoked_at timestamptz,
    CONSTRAINT uq_refresh_tokens_hash UNIQUE (token_hash),
    CONSTRAINT uq_refresh_tokens_generation UNIQUE (session_id, generation),
    CONSTRAINT ck_refresh_tokens_expiry CHECK (expires_at > issued_at)
);
```

**Индексы:**

- `CREATE INDEX ix_refresh_tokens_session_active ON iam.refresh_tokens (session_id, generation DESC) WHERE consumed_at IS NULL AND revoked_at IS NULL;`

## 6A.12. `iam.password_reset_tokens`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE iam.password_reset_tokens (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    token_hash bytea NOT NULL CHECK (octet_length(token_hash) = 32),
    created_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz,
    CONSTRAINT uq_password_reset_token_hash UNIQUE (token_hash),
    CONSTRAINT ck_password_reset_expiry CHECK (expires_at > created_at)
);
```

**Индексы:**

- `CREATE INDEX ix_password_reset_user_active ON iam.password_reset_tokens (user_account_id, expires_at) WHERE consumed_at IS NULL;`

## 6A.13. `iam.login_attempts`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE iam.login_attempts (
    id uuid PRIMARY KEY,
    organization_id uuid REFERENCES core.organizations(id) ON DELETE SET NULL,
    login_normalized citext NOT NULL,
    user_account_id uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    device_id uuid REFERENCES iam.devices(id) ON DELETE SET NULL,
    ip_address inet,
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    succeeded boolean NOT NULL,
    failure_code varchar(40),
    correlation_id uuid NOT NULL
);
```

**Индексы:**

- `CREATE INDEX ix_login_attempts_login_time ON iam.login_attempts (login_normalized, occurred_at DESC);`
- `CREATE INDEX ix_login_attempts_ip_time ON iam.login_attempts (ip_address, occurred_at DESC);`

## 6A.14. `iam.permissions`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE iam.permissions (
    id uuid PRIMARY KEY,
    code citext NOT NULL UNIQUE,
    resource varchar(60) NOT NULL,
    action varchar(60) NOT NULL,
    description text NOT NULL,
    is_sensitive boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_permissions_resource_action UNIQUE (resource, action)
);
```

## 6A.15. `iam.roles`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE iam.roles (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    code citext NOT NULL,
    name text NOT NULL,
    scope_type varchar(20) NOT NULL CHECK (scope_type IN ('organization','department')),
    is_system boolean NOT NULL DEFAULT false,
    description text,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_roles_org_code UNIQUE (organization_id, code)
);
```

## 6A.16. `iam.role_permissions`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE iam.role_permissions (
    role_id uuid NOT NULL REFERENCES iam.roles(id) ON DELETE CASCADE,
    permission_id uuid NOT NULL REFERENCES iam.permissions(id) ON DELETE CASCADE,
    effect varchar(8) NOT NULL DEFAULT 'allow' CHECK (effect IN ('allow','deny')),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (role_id, permission_id)
);
```

**Индексы:**

- `CREATE INDEX ix_role_permissions_permission ON iam.role_permissions (permission_id, role_id);`

## 6A.17. `iam.user_roles`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE iam.user_roles (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    role_id uuid NOT NULL REFERENCES iam.roles(id) ON DELETE CASCADE,
    department_id uuid REFERENCES org.departments(id) ON DELETE CASCADE,
    valid_from timestamptz NOT NULL DEFAULT clock_timestamp(),
    valid_until timestamptz,
    granted_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_user_roles_scope UNIQUE NULLS NOT DISTINCT (user_account_id, role_id, department_id),
    CONSTRAINT ck_user_roles_validity CHECK (valid_until IS NULL OR valid_until > valid_from)
);
```

**Индексы:**

- `CREATE INDEX ix_user_roles_user_active ON iam.user_roles (organization_id, user_account_id, valid_until);`

## 6A.18. `iam.department_managers`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE iam.department_managers (
    department_id uuid NOT NULL REFERENCES org.departments(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    granted_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (department_id, user_account_id)
);
```

## 6A.19. `iam.explicit_access_rules`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE iam.explicit_access_rules (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    object_id uuid NOT NULL REFERENCES core.objects(id) ON DELETE CASCADE,
    principal_type varchar(20) NOT NULL CHECK (principal_type IN ('user','role','department')),
    principal_id uuid NOT NULL,
    permission_id uuid NOT NULL REFERENCES iam.permissions(id) ON DELETE CASCADE,
    effect varchar(8) NOT NULL CHECK (effect IN ('allow','deny')),
    reason text,
    valid_until timestamptz,
    created_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_explicit_access_rule UNIQUE (object_id, principal_type, principal_id, permission_id)
);
```

**Индексы:**

- `CREATE INDEX ix_explicit_access_principal ON iam.explicit_access_rules (organization_id, principal_type, principal_id, object_id);`
- `CREATE INDEX ix_explicit_access_object ON iam.explicit_access_rules (organization_id, object_id, permission_id);`

## 6A.20. `iam.authorization_scope_versions`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE iam.authorization_scope_versions (
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    scope_version bigint NOT NULL DEFAULT 1 CHECK (scope_version > 0),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (organization_id, user_account_id)
);
```

## 6A.21. `projects.project_roles`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE projects.project_roles (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    code citext NOT NULL,
    name text NOT NULL,
    is_system boolean NOT NULL DEFAULT false,
    description text,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    CONSTRAINT uq_project_roles_org_code UNIQUE (organization_id, code)
);
```

## 6A.22. `projects.project_role_permissions`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE projects.project_role_permissions (
    project_role_id uuid NOT NULL REFERENCES projects.project_roles(id) ON DELETE CASCADE,
    permission_id uuid NOT NULL REFERENCES iam.permissions(id) ON DELETE CASCADE,
    effect varchar(8) NOT NULL DEFAULT 'allow' CHECK (effect IN ('allow','deny')),
    PRIMARY KEY (project_role_id, permission_id)
);
```

## 6A.23. `projects.projects`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE projects.projects (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    name text NOT NULL,
    description text,
    owner_user_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    manager_user_id uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    status varchar(20) NOT NULL DEFAULT 'planning' CHECK (status IN ('planning','active','paused','completed')),
    start_date date,
    planned_end_date date,
    actual_end_at timestamptz,
    default_time_zone text,
    color_code varchar(9),
    CONSTRAINT fk_projects_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT ck_projects_name CHECK (length(btrim(name)) BETWEEN 1 AND 300),
    CONSTRAINT ck_projects_dates CHECK (planned_end_date IS NULL OR start_date IS NULL OR planned_end_date >= start_date),
    CONSTRAINT ck_projects_actual_end CHECK (status <> 'completed' OR actual_end_at IS NOT NULL)
);
```

**Индексы:**

- `CREATE INDEX ix_projects_org_status ON projects.projects (organization_id, status, planned_end_date, name);`
- `CREATE INDEX ix_projects_owner ON projects.projects (organization_id, owner_user_id, status);`
- `CREATE INDEX ix_projects_manager ON projects.projects (organization_id, manager_user_id, status) WHERE manager_user_id IS NOT NULL;`
- `CREATE INDEX ix_projects_name_trgm ON projects.projects USING gin (name gin_trgm_ops);`

## 6A.24. `projects.project_members`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE projects.project_members (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    project_id uuid NOT NULL REFERENCES projects.projects(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    project_role_id uuid NOT NULL REFERENCES projects.project_roles(id) ON DELETE RESTRICT,
    status varchar(16) NOT NULL DEFAULT 'active' CHECK (status IN ('invited','active','removed')),
    joined_at timestamptz,
    removed_at timestamptz,
    added_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_project_members_user UNIQUE (project_id, user_account_id),
    CONSTRAINT ck_project_members_status_dates CHECK ((status <> 'active' OR joined_at IS NOT NULL) AND (status <> 'removed' OR removed_at IS NOT NULL))
);
```

**Индексы:**

- `CREATE INDEX ix_project_members_user_active ON projects.project_members (organization_id, user_account_id, status, project_id);`
- `CREATE INDEX ix_project_members_project_active ON projects.project_members (project_id, status, user_account_id);`

## 6A.25. `projects.project_member_permission_overrides`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE projects.project_member_permission_overrides (
    project_member_id uuid NOT NULL REFERENCES projects.project_members(id) ON DELETE CASCADE,
    permission_id uuid NOT NULL REFERENCES iam.permissions(id) ON DELETE CASCADE,
    effect varchar(8) NOT NULL CHECK (effect IN ('allow','deny')),
    reason text,
    created_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (project_member_id, permission_id)
);
```

## 6A.26. `work.inbox_items`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE work.inbox_items (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    owner_user_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    item_type varchar(20) NOT NULL CHECK (item_type IN ('task','note','file_link','web_link','idea','assignment')),
    title text,
    content text,
    raw_url text,
    raw_path text,
    status varchar(20) NOT NULL DEFAULT 'unprocessed' CHECK (status IN ('unprocessed','converted','discarded')),
    converted_object_id uuid REFERENCES core.objects(id) ON DELETE SET NULL,
    processed_at timestamptz,
    CONSTRAINT fk_inbox_items_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT ck_inbox_content CHECK (coalesce(length(btrim(title)),0) > 0 OR coalesce(length(btrim(content)),0) > 0 OR raw_url IS NOT NULL OR raw_path IS NOT NULL)
);
```

**Индексы:**

- `CREATE INDEX ix_inbox_owner_status ON work.inbox_items (organization_id, owner_user_id, status, id);`

## 6A.27. `work.recurrence_series`

**Назначение:** Шаблоны повторений.

```sql
CREATE TABLE work.recurrence_series (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    source_task_id uuid,
    status varchar(16) NOT NULL DEFAULT 'active' CHECK (status IN ('active','paused','completed','cancelled')),
    frequency varchar(16) NOT NULL CHECK (frequency IN ('daily','weekly','monthly','yearly')),
    interval_value smallint NOT NULL DEFAULT 1 CHECK (interval_value BETWEEN 1 AND 999),
    by_weekdays smallint[] CHECK (by_weekdays IS NULL OR by_weekdays <@ ARRAY[1,2,3,4,5,6,7]::smallint[]),
    by_month_days smallint[] CHECK (by_month_days IS NULL OR by_month_days <@ ARRAY[-31,-30,-29,-28,-27,-26,-25,-24,-23,-22,-21,-20,-19,-18,-17,-16,-15,-14,-13,-12,-11,-10,-9,-8,-7,-6,-5,-4,-3,-2,-1,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31]::smallint[]),
    month_of_year smallint CHECK (month_of_year BETWEEN 1 AND 12),
    occurrence_start_date date NOT NULL,
    local_start_time time,
    time_zone text NOT NULL,
    duration_minutes integer CHECK (duration_minutes BETWEEN 1 AND 10080),
    deadline_offset_minutes integer,
    until_date date,
    max_occurrences integer CHECK (max_occurrences IS NULL OR max_occurrences > 0),
    generation_horizon_days integer NOT NULL DEFAULT 90 CHECK (generation_horizon_days BETWEEN 7 AND 730),
    dst_gap_policy varchar(20) NOT NULL DEFAULT 'shift_forward' CHECK (dst_gap_policy IN ('shift_forward','skip')),
    dst_overlap_policy varchar(20) NOT NULL DEFAULT 'earlier_offset' CHECK (dst_overlap_policy IN ('earlier_offset','later_offset')),
    next_generation_date date NOT NULL,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    created_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_recurrence_end CHECK (until_date IS NULL OR until_date >= occurrence_start_date)
);
```

**Индексы:**

- `CREATE INDEX ix_recurrence_due_generation ON work.recurrence_series (organization_id, status, next_generation_date) WHERE status = 'active';`

## 6A.28. `work.tasks`

**Назначение:** Задачи и подзадачи.

```sql
CREATE TABLE work.tasks (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    project_id uuid REFERENCES projects.projects(id) ON DELETE RESTRICT,
    parent_task_id uuid REFERENCES work.tasks(id) ON DELETE RESTRICT,
    title text NOT NULL,
    description text,
    author_user_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    creator_user_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    requester_user_id uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    primary_counterparty_object_id uuid REFERENCES core.objects(id) ON DELETE SET NULL,
    status varchar(20) NOT NULL DEFAULT 'new' CHECK (status IN ('new','in_progress','review','completed','cancelled')),
    priority varchar(16) NOT NULL DEFAULT 'normal' CHECK (priority IN ('low','normal','high','critical')),
    scheduled_date date,
    start_time_local time,
    schedule_time_zone text,
    start_at_utc timestamptz,
    planned_duration_minutes integer CHECK (planned_duration_minutes BETWEEN 1 AND 10080),
    deadline_at timestamptz,
    completed_at timestamptz,
    cancelled_at timestamptz,
    sort_order numeric(20,10) NOT NULL DEFAULT 0,
    recurrence_series_id uuid REFERENCES work.recurrence_series(id) ON DELETE SET NULL,
    recurrence_occurrence_key varchar(64),
    is_recurrence_exception boolean NOT NULL DEFAULT false,
    CONSTRAINT fk_tasks_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT fk_tasks_primary_counterparty_org FOREIGN KEY (organization_id, primary_counterparty_object_id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT ck_tasks_title CHECK (length(btrim(title)) BETWEEN 1 AND 500),
    CONSTRAINT ck_tasks_schedule CHECK (
        (start_time_local IS NULL AND start_at_utc IS NULL) OR
        (scheduled_date IS NOT NULL AND start_time_local IS NOT NULL AND schedule_time_zone IS NOT NULL AND start_at_utc IS NOT NULL)
    ),
    CONSTRAINT ck_tasks_completed CHECK ((status = 'completed' AND completed_at IS NOT NULL) OR status <> 'completed'),
    CONSTRAINT ck_tasks_cancelled CHECK ((status = 'cancelled' AND cancelled_at IS NOT NULL) OR status <> 'cancelled'),
    CONSTRAINT ck_tasks_parent_not_self CHECK (parent_task_id IS NULL OR parent_task_id <> id),
    CONSTRAINT uq_tasks_recurrence_occurrence UNIQUE (recurrence_series_id, recurrence_occurrence_key)
);
```

**Индексы:**

- `CREATE INDEX ix_tasks_org_project_status ON work.tasks (organization_id, project_id, status, scheduled_date, deadline_at);`
- `CREATE INDEX ix_tasks_org_schedule ON work.tasks (organization_id, scheduled_date, start_at_utc) WHERE scheduled_date IS NOT NULL;`
- `CREATE INDEX ix_tasks_org_deadline_open ON work.tasks (organization_id, deadline_at, priority) WHERE status NOT IN ('completed','cancelled') AND deadline_at IS NOT NULL;`
- `CREATE INDEX ix_tasks_parent_sort ON work.tasks (parent_task_id, sort_order, id) WHERE parent_task_id IS NOT NULL;`
- `CREATE INDEX ix_tasks_title_trgm ON work.tasks USING gin (title gin_trgm_ops);`
- `CREATE INDEX ix_tasks_recurrence_series ON work.tasks (recurrence_series_id, scheduled_date) WHERE recurrence_series_id IS NOT NULL;`

## 6A.29. `work.task_assignees`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE work.task_assignees (
    task_id uuid NOT NULL REFERENCES work.tasks(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    is_primary boolean NOT NULL DEFAULT false,
    assigned_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    assigned_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (task_id, user_account_id)
);
```

**Индексы:**

- `CREATE UNIQUE INDEX uq_task_one_primary_assignee ON work.task_assignees (task_id) WHERE is_primary;`
- `CREATE INDEX ix_task_assignees_user ON work.task_assignees (user_account_id, task_id);`

## 6A.30. `work.task_watchers`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE work.task_watchers (
    task_id uuid NOT NULL REFERENCES work.tasks(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    added_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    added_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (task_id, user_account_id)
);
```

**Индексы:**

- `CREATE INDEX ix_task_watchers_user ON work.task_watchers (user_account_id, task_id);`

## 6A.31. `work.task_dependencies`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE work.task_dependencies (
    predecessor_task_id uuid NOT NULL REFERENCES work.tasks(id) ON DELETE CASCADE,
    successor_task_id uuid NOT NULL REFERENCES work.tasks(id) ON DELETE CASCADE,
    dependency_type varchar(24) NOT NULL DEFAULT 'finish_to_start' CHECK (dependency_type IN ('finish_to_start')),
    created_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (predecessor_task_id, successor_task_id),
    CONSTRAINT ck_task_dependency_not_self CHECK (predecessor_task_id <> successor_task_id)
);
```

**Индексы:**

- `CREATE INDEX ix_task_dependencies_successor ON work.task_dependencies (successor_task_id, predecessor_task_id);`

## 6A.32. `work.checklists`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE work.checklists (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    task_id uuid NOT NULL REFERENCES work.tasks(id) ON DELETE CASCADE,
    title text NOT NULL DEFAULT 'Чек-лист',
    sort_order numeric(20,10) NOT NULL DEFAULT 0,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    created_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_checklists_title CHECK (length(btrim(title)) BETWEEN 1 AND 200)
);
```

**Индексы:**

- `CREATE INDEX ix_checklists_task_sort ON work.checklists (task_id, sort_order, id);`

## 6A.33. `work.checklist_items`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE work.checklist_items (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    checklist_id uuid NOT NULL REFERENCES work.checklists(id) ON DELETE CASCADE,
    text text NOT NULL,
    is_completed boolean NOT NULL DEFAULT false,
    sort_order numeric(20,10) NOT NULL DEFAULT 0,
    completed_at timestamptz,
    completed_by uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    created_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_checklist_items_text CHECK (length(btrim(text)) BETWEEN 1 AND 1000),
    CONSTRAINT ck_checklist_items_completion CHECK ((is_completed AND completed_at IS NOT NULL AND completed_by IS NOT NULL) OR (NOT is_completed AND completed_at IS NULL))
);
```

**Индексы:**

- `CREATE INDEX ix_checklist_items_checklist_sort ON work.checklist_items (checklist_id, sort_order, id);`

## 6A.34. `work.recurrence_occurrences`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE work.recurrence_occurrences (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    series_id uuid NOT NULL REFERENCES work.recurrence_series(id) ON DELETE CASCADE,
    occurrence_key varchar(64) NOT NULL,
    local_date date NOT NULL,
    local_start_time time,
    start_at_utc timestamptz,
    generated_task_id uuid REFERENCES work.tasks(id) ON DELETE SET NULL,
    status varchar(20) NOT NULL CHECK (status IN ('planned','generated','skipped','cancelled','failed')),
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    last_error_code varchar(80),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_recurrence_occurrence UNIQUE (series_id, occurrence_key)
);
```

**Индексы:**

- `CREATE INDEX ix_recurrence_occurrences_series_date ON work.recurrence_occurrences (series_id, local_date, status);`

## 6A.35. `work.recurrence_exceptions`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE work.recurrence_exceptions (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    series_id uuid NOT NULL REFERENCES work.recurrence_series(id) ON DELETE CASCADE,
    occurrence_key varchar(64) NOT NULL,
    exception_type varchar(20) NOT NULL CHECK (exception_type IN ('modified','cancelled','detached')),
    replacement_task_id uuid REFERENCES work.tasks(id) ON DELETE SET NULL,
    override_payload jsonb,
    created_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_recurrence_exception UNIQUE (series_id, occurrence_key)
);
```

## 6A.36. `calendar.calendar_events`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE calendar.calendar_events (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    project_id uuid REFERENCES projects.projects(id) ON DELETE RESTRICT,
    title text NOT NULL,
    description text,
    organizer_user_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    event_date date NOT NULL,
    is_all_day boolean NOT NULL DEFAULT false,
    start_time_local time,
    end_time_local time,
    time_zone text NOT NULL,
    start_at_utc timestamptz,
    end_at_utc timestamptz,
    location_text text,
    status varchar(16) NOT NULL DEFAULT 'scheduled' CHECK (status IN ('scheduled','cancelled','completed')),
    recurrence_series_id uuid REFERENCES work.recurrence_series(id) ON DELETE SET NULL,
    CONSTRAINT fk_calendar_events_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT ck_calendar_event_title CHECK (length(btrim(title)) BETWEEN 1 AND 500),
    CONSTRAINT ck_calendar_event_times CHECK (
        (is_all_day AND start_time_local IS NULL AND end_time_local IS NULL AND start_at_utc IS NULL AND end_at_utc IS NULL) OR
        (NOT is_all_day AND start_time_local IS NOT NULL AND end_time_local IS NOT NULL AND start_at_utc IS NOT NULL AND end_at_utc IS NOT NULL AND end_at_utc > start_at_utc)
    )
);
```

**Индексы:**

- `CREATE INDEX ix_calendar_events_org_range ON calendar.calendar_events (organization_id, start_at_utc, end_at_utc) WHERE status <> 'cancelled';`
- `CREATE INDEX ix_calendar_events_org_date ON calendar.calendar_events (organization_id, event_date, is_all_day) WHERE status <> 'cancelled';`
- `CREATE INDEX ix_calendar_events_project ON calendar.calendar_events (project_id, event_date) WHERE project_id IS NOT NULL;`

## 6A.37. `calendar.event_attendees`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE calendar.event_attendees (
    event_id uuid NOT NULL REFERENCES calendar.calendar_events(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    attendee_role varchar(16) NOT NULL DEFAULT 'required' CHECK (attendee_role IN ('required','optional','observer')),
    response_status varchar(16) NOT NULL DEFAULT 'pending' CHECK (response_status IN ('pending','accepted','declined','tentative')),
    responded_at timestamptz,
    PRIMARY KEY (event_id, user_account_id)
);
```

**Индексы:**

- `CREATE INDEX ix_event_attendees_user ON calendar.event_attendees (user_account_id, event_id, response_status);`

## 6A.38. `calendar.reminders`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE calendar.reminders (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    target_object_id uuid NOT NULL REFERENCES core.objects(id) ON DELETE CASCADE,
    recipient_user_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    trigger_type varchar(24) NOT NULL CHECK (trigger_type IN ('absolute','before_start','before_deadline','at_start','at_deadline')),
    offset_minutes integer,
    absolute_trigger_at timestamptz,
    next_trigger_at timestamptz NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'scheduled' CHECK (status IN ('scheduled','due','delivered','snoozed','cancelled','expired')),
    snooze_count integer NOT NULL DEFAULT 0 CHECK (snooze_count BETWEEN 0 AND 100),
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    created_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_reminder_trigger CHECK (
        (trigger_type = 'absolute' AND absolute_trigger_at IS NOT NULL AND offset_minutes IS NULL) OR
        (trigger_type <> 'absolute' AND absolute_trigger_at IS NULL)
    )
);
```

**Индексы:**

- `CREATE INDEX ix_reminders_due ON calendar.reminders (next_trigger_at, id) WHERE status IN ('scheduled','snoozed');`
- `CREATE INDEX ix_reminders_recipient ON calendar.reminders (organization_id, recipient_user_id, status, next_trigger_at);`
- `CREATE INDEX ix_reminders_target ON calendar.reminders (target_object_id, recipient_user_id);`

## 6A.39. `calendar.reminder_occurrences`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE calendar.reminder_occurrences (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    reminder_id uuid NOT NULL REFERENCES calendar.reminders(id) ON DELETE CASCADE,
    due_at timestamptz NOT NULL,
    status varchar(16) NOT NULL CHECK (status IN ('created','claimed','delivered','failed','cancelled')),
    claimed_by_worker text,
    claimed_at timestamptz,
    delivered_at timestamptz,
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    last_error_code varchar(80),
    idempotency_key varchar(160) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_reminder_occurrence_key UNIQUE (idempotency_key)
);
```

**Индексы:**

- `CREATE INDEX ix_reminder_occurrences_claim ON calendar.reminder_occurrences (status, due_at) WHERE status IN ('created','failed');`

## 6A.40. `files.network_resources`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE files.network_resources (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    name text NOT NULL,
    root_unc_path text NOT NULL,
    normalized_root_path text NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'active' CHECK (status IN ('active','degraded','unavailable','retired')),
    allow_write_metadata boolean NOT NULL DEFAULT true,
    last_health_at timestamptz,
    last_health_code varchar(60),
    CONSTRAINT fk_network_resources_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT uq_network_resources_root UNIQUE (organization_id, normalized_root_path),
    CONSTRAINT ck_network_resources_unc CHECK (root_unc_path LIKE '\\\\%')
);
```

**Индексы:**

- `CREATE INDEX ix_network_resources_org_status ON files.network_resources (organization_id, status, name);`

## 6A.41. `files.catalog_items`

**Назначение:** Виртуальное дерево и logical file metadata.

```sql
CREATE TABLE files.catalog_items (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    parent_item_id uuid REFERENCES files.catalog_items(id) ON DELETE RESTRICT,
    item_type varchar(24) NOT NULL CHECK (item_type IN ('virtual_folder','file_reference','folder_reference','web_link','text_note')),
    name text NOT NULL,
    description text,
    note_content text,
    web_url text,
    mime_type varchar(200),
    file_extension varchar(32),
    observed_size_bytes bigint CHECK (observed_size_bytes IS NULL OR observed_size_bytes >= 0),
    observed_modified_at timestamptz,
    sort_order numeric(20,10) NOT NULL DEFAULT 0,
    created_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    CONSTRAINT fk_catalog_items_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT ck_catalog_items_name CHECK (length(btrim(name)) BETWEEN 1 AND 500),
    CONSTRAINT ck_catalog_items_content CHECK (
        (item_type = 'web_link' AND web_url IS NOT NULL AND note_content IS NULL) OR
        (item_type = 'text_note' AND note_content IS NOT NULL AND web_url IS NULL) OR
        (item_type NOT IN ('web_link','text_note') AND web_url IS NULL AND note_content IS NULL)
    ),
    CONSTRAINT ck_catalog_items_parent_not_self CHECK (parent_item_id IS NULL OR parent_item_id <> id)
);
```

**Индексы:**

- `CREATE INDEX ix_catalog_items_parent_sort ON files.catalog_items (organization_id, parent_item_id, sort_order, name);`
- `CREATE INDEX ix_catalog_items_type_state ON files.catalog_items (organization_id, item_type, id);`
- `CREATE INDEX ix_catalog_items_name_trgm ON files.catalog_items USING gin (name gin_trgm_ops);`

## 6A.42. `files.file_locations`

**Назначение:** Несколько физических путей одного logical item.

```sql
CREATE TABLE files.file_locations (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    catalog_item_id uuid NOT NULL REFERENCES files.catalog_items(id) ON DELETE CASCADE,
    location_type varchar(20) NOT NULL CHECK (location_type IN ('local_path','unc_path','mapped_drive')),
    raw_path text NOT NULL,
    normalized_path text NOT NULL,
    device_id uuid REFERENCES iam.devices(id) ON DELETE SET NULL,
    network_resource_id uuid REFERENCES files.network_resources(id) ON DELETE SET NULL,
    priority smallint NOT NULL DEFAULT 100 CHECK (priority BETWEEN 0 AND 32767),
    is_enabled boolean NOT NULL DEFAULT true,
    is_primary boolean NOT NULL DEFAULT false,
    availability_status varchar(24) NOT NULL DEFAULT 'unknown' CHECK (availability_status IN ('unknown','available','not_found','access_denied','resource_unavailable','invalid_path')),
    last_checked_at timestamptz,
    last_checked_by_device_id uuid REFERENCES iam.devices(id) ON DELETE SET NULL,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    created_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_file_locations_scope_path UNIQUE NULLS NOT DISTINCT (catalog_item_id, device_id, normalized_path),
    CONSTRAINT ck_file_locations_scope CHECK (
        (location_type = 'local_path' AND device_id IS NOT NULL AND network_resource_id IS NULL) OR
        (location_type = 'mapped_drive' AND device_id IS NOT NULL) OR
        (location_type = 'unc_path' AND device_id IS NULL)
    )
);
```

**Индексы:**

- `CREATE UNIQUE INDEX uq_file_location_one_primary_scope ON files.file_locations (catalog_item_id, coalesce(device_id, '00000000-0000-0000-0000-000000000000'::uuid)) WHERE is_primary AND is_enabled;`
- `CREATE INDEX ix_file_locations_item_rank ON files.file_locations (catalog_item_id, is_enabled DESC, priority, id);`
- `CREATE INDEX ix_file_locations_device ON files.file_locations (device_id, is_enabled, priority) WHERE device_id IS NOT NULL;`
- `CREATE INDEX ix_file_locations_network ON files.file_locations (network_resource_id, is_enabled, priority) WHERE network_resource_id IS NOT NULL;`
- `CREATE INDEX ix_file_locations_path_trgm ON files.file_locations USING gin (normalized_path gin_trgm_ops);`

## 6A.43. `files.file_location_checks`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE files.file_location_checks (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    file_location_id uuid NOT NULL REFERENCES files.file_locations(id) ON DELETE CASCADE,
    device_id uuid NOT NULL REFERENCES iam.devices(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    checked_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    status varchar(24) NOT NULL CHECK (status IN ('available','not_found','access_denied','resource_unavailable','invalid_path','timeout')),
    latency_ms integer CHECK (latency_ms IS NULL OR latency_ms >= 0),
    os_error_code varchar(80),
    sanitized_detail text
);
```

**Индексы:**

- `CREATE INDEX ix_file_location_checks_location_time ON files.file_location_checks (file_location_id, checked_at DESC);`
- `CREATE INDEX ix_file_location_checks_device_time ON files.file_location_checks (device_id, checked_at DESC);`

## 6A.44. `crm.companies`

**Назначение:** Контрагенты-компании.

```sql
CREATE TABLE crm.companies (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    name text NOT NULL,
    legal_name text,
    industry text,
    website text,
    tax_identifier text,
    notes text,
    status varchar(16) NOT NULL DEFAULT 'active' CHECK (status IN ('active','inactive')),
    CONSTRAINT fk_companies_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT ck_companies_name CHECK (length(btrim(name)) BETWEEN 1 AND 500)
);
```

**Индексы:**

- `CREATE INDEX ix_companies_org_status_name ON crm.companies (organization_id, status, name);`
- `CREATE INDEX ix_companies_name_trgm ON crm.companies USING gin (name gin_trgm_ops);`

## 6A.45. `crm.contacts`

**Назначение:** Физические лица.

```sql
CREATE TABLE crm.contacts (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    first_name text NOT NULL,
    last_name text,
    middle_name text,
    display_name text NOT NULL,
    notes text,
    status varchar(16) NOT NULL DEFAULT 'active' CHECK (status IN ('active','inactive')),
    CONSTRAINT fk_contacts_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT ck_contacts_name CHECK (length(btrim(first_name)) BETWEEN 1 AND 100 AND length(btrim(display_name)) BETWEEN 1 AND 300)
);
```

**Индексы:**

- `CREATE INDEX ix_contacts_org_status_name ON crm.contacts (organization_id, status, display_name);`
- `CREATE INDEX ix_contacts_display_trgm ON crm.contacts USING gin (display_name gin_trgm_ops);`

## 6A.46. `crm.contact_company_roles`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE crm.contact_company_roles (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    contact_id uuid NOT NULL REFERENCES crm.contacts(id) ON DELETE CASCADE,
    company_id uuid NOT NULL REFERENCES crm.companies(id) ON DELETE CASCADE,
    job_title text,
    department_name text,
    is_primary boolean NOT NULL DEFAULT false,
    valid_from date,
    valid_to date,
    notes text,
    CONSTRAINT uq_contact_company_role UNIQUE NULLS NOT DISTINCT (contact_id, company_id, job_title, valid_from),
    CONSTRAINT ck_contact_company_dates CHECK (valid_to IS NULL OR valid_from IS NULL OR valid_to >= valid_from)
);
```

**Индексы:**

- `CREATE INDEX ix_contact_company_roles_company ON crm.contact_company_roles (company_id, is_primary DESC, contact_id);`

## 6A.47. `crm.communication_channels`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE crm.communication_channels (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    owner_object_id uuid NOT NULL REFERENCES core.objects(id) ON DELETE CASCADE,
    channel_type varchar(20) NOT NULL CHECK (channel_type IN ('phone','email','telegram','whatsapp','viber','other_messenger','website')),
    label text,
    value_raw text NOT NULL,
    value_normalized text NOT NULL,
    is_primary boolean NOT NULL DEFAULT false,
    is_verified boolean NOT NULL DEFAULT false,
    sort_order integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_communication_channel UNIQUE (owner_object_id, channel_type, value_normalized)
);
```

**Индексы:**

- `CREATE INDEX ix_communication_channels_owner ON crm.communication_channels (owner_object_id, is_primary DESC, sort_order);`
- `CREATE INDEX ix_communication_channels_value_trgm ON crm.communication_channels USING gin (value_normalized gin_trgm_ops);`

## 6A.48. `crm.addresses`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE crm.addresses (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    owner_object_id uuid NOT NULL REFERENCES core.objects(id) ON DELETE CASCADE,
    address_type varchar(20) NOT NULL DEFAULT 'work' CHECK (address_type IN ('work','legal','postal','other')),
    country_code char(2),
    region text,
    city text,
    street text,
    postal_code text,
    formatted_address text NOT NULL,
    is_primary boolean NOT NULL DEFAULT false,
    sort_order integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_addresses_formatted CHECK (length(btrim(formatted_address)) BETWEEN 1 AND 1000)
);
```

**Индексы:**

- `CREATE INDEX ix_addresses_owner ON crm.addresses (owner_object_id, is_primary DESC, sort_order);`
- `CREATE INDEX ix_addresses_formatted_trgm ON crm.addresses USING gin (formatted_address gin_trgm_ops);`

## 6A.49. `crm.interactions`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE crm.interactions (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    counterparty_object_id uuid NOT NULL REFERENCES core.objects(id) ON DELETE RESTRICT,
    interaction_type varchar(20) NOT NULL CHECK (interaction_type IN ('call','meeting','email','agreement','note','next_step')),
    occurred_at timestamptz NOT NULL,
    subject text NOT NULL,
    details text,
    next_step text,
    next_step_due_at timestamptz,
    created_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    CONSTRAINT fk_interactions_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT fk_interactions_counterparty_org FOREIGN KEY (organization_id, counterparty_object_id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT ck_interactions_subject CHECK (length(btrim(subject)) BETWEEN 1 AND 500)
);
```

**Индексы:**

- `CREATE INDEX ix_interactions_counterparty_time ON crm.interactions (counterparty_object_id, occurred_at DESC);`
- `CREATE INDEX ix_interactions_org_type_time ON crm.interactions (organization_id, interaction_type, occurred_at DESC);`

## 6A.50. `crm.interaction_participants`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE crm.interaction_participants (
    interaction_id uuid NOT NULL REFERENCES crm.interactions(id) ON DELETE CASCADE,
    participant_object_id uuid NOT NULL REFERENCES core.objects(id) ON DELETE RESTRICT,
    participant_role varchar(20) NOT NULL DEFAULT 'participant' CHECK (participant_role IN ('participant','organizer','observer')),
    PRIMARY KEY (interaction_id, participant_object_id)
);
```

**Индексы:**

- `CREATE INDEX ix_interaction_participants_object ON crm.interaction_participants (participant_object_id, interaction_id);`

## 6A.51. `collab.comments`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE collab.comments (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    target_object_id uuid NOT NULL REFERENCES core.objects(id) ON DELETE CASCADE,
    author_user_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    body text NOT NULL,
    status varchar(16) NOT NULL DEFAULT 'active' CHECK (status IN ('active','deleted')),
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    deleted_at timestamptz,
    deleted_by uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    CONSTRAINT ck_comments_body CHECK (length(btrim(body)) BETWEEN 1 AND 20000),
    CONSTRAINT ck_comments_deleted CHECK ((status = 'deleted' AND deleted_at IS NOT NULL) OR status = 'active')
);
```

**Индексы:**

- `CREATE INDEX ix_comments_target_time ON collab.comments (target_object_id, created_at, id) WHERE status = 'active';`
- `CREATE INDEX ix_comments_author_time ON collab.comments (author_user_id, created_at DESC);`

## 6A.52. `collab.comment_versions`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE collab.comment_versions (
    id uuid PRIMARY KEY,
    comment_id uuid NOT NULL REFERENCES collab.comments(id) ON DELETE CASCADE,
    version bigint NOT NULL,
    body text NOT NULL,
    changed_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    changed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    change_reason text,
    CONSTRAINT uq_comment_versions UNIQUE (comment_id, version)
);
```

## 6A.53. `collab.tags`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE collab.tags (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    name citext NOT NULL,
    color_code varchar(9),
    description text,
    created_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    CONSTRAINT fk_tags_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT uq_tags_org_name UNIQUE (organization_id, name),
    CONSTRAINT ck_tags_name CHECK (length(btrim(name::text)) BETWEEN 1 AND 100)
);
```

## 6A.54. `collab.object_tags`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE collab.object_tags (
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    object_id uuid NOT NULL REFERENCES core.objects(id) ON DELETE CASCADE,
    tag_id uuid NOT NULL REFERENCES collab.tags(id) ON DELETE CASCADE,
    added_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    added_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (object_id, tag_id)
);
```

**Индексы:**

- `CREATE INDEX ix_object_tags_tag ON collab.object_tags (tag_id, object_id);`

## 6A.55. `collab.object_links`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE collab.object_links (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    source_object_id uuid NOT NULL REFERENCES core.objects(id) ON DELETE CASCADE,
    target_object_id uuid NOT NULL REFERENCES core.objects(id) ON DELETE CASCADE,
    link_type varchar(32) NOT NULL CHECK (link_type IN ('related','task_file','project_file','contact_file','task_contact','project_contact','task_project','parent_reference')),
    created_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_object_link UNIQUE (source_object_id, target_object_id, link_type),
    CONSTRAINT ck_object_link_not_self CHECK (source_object_id <> target_object_id)
);
```

**Индексы:**

- `CREATE INDEX ix_object_links_target ON collab.object_links (target_object_id, link_type, source_object_id);`
- `CREATE INDEX ix_object_links_source ON collab.object_links (source_object_id, link_type, target_object_id);`

## 6A.56. `notify.notification_preferences`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE notify.notification_preferences (
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    notification_type varchar(40) NOT NULL,
    enabled boolean NOT NULL DEFAULT true,
    desktop_enabled boolean NOT NULL DEFAULT true,
    sound_enabled boolean NOT NULL DEFAULT true,
    default_snooze_minutes integer NOT NULL DEFAULT 15 CHECK (default_snooze_minutes BETWEEN 1 AND 10080),
    quiet_hours_start time,
    quiet_hours_end time,
    quiet_hours_time_zone text,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (user_account_id, notification_type)
);
```

## 6A.57. `notify.notifications`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE notify.notifications (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL,
    recipient_user_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    notification_type varchar(40) NOT NULL,
    source_object_id uuid REFERENCES core.objects(id) ON DELETE SET NULL,
    title text NOT NULL,
    body text NOT NULL,
    severity varchar(12) NOT NULL DEFAULT 'info' CHECK (severity IN ('info','warning','critical')),
    status varchar(16) NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','delivered','read','dismissed','failed','expired')),
    not_before timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz,
    delivered_at timestamptz,
    read_at timestamptz,
    dismissed_at timestamptz,
    deduplication_key varchar(200),
    action_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT fk_notifications_object_org FOREIGN KEY (organization_id, id) REFERENCES core.objects(organization_id, id),
    CONSTRAINT ck_notifications_title CHECK (length(btrim(title)) BETWEEN 1 AND 500)
);
```

**Индексы:**

- `CREATE INDEX ix_notifications_recipient_status ON notify.notifications (organization_id, recipient_user_id, status, not_before DESC);`
- `CREATE INDEX ix_notifications_delivery_due ON notify.notifications (not_before, id) WHERE status = 'pending';`
- `CREATE UNIQUE INDEX uq_notifications_dedupe ON notify.notifications (recipient_user_id, deduplication_key) WHERE deduplication_key IS NOT NULL;`

## 6A.58. `notify.notification_deliveries`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE notify.notification_deliveries (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    notification_id uuid NOT NULL REFERENCES notify.notifications(id) ON DELETE CASCADE,
    device_id uuid REFERENCES iam.devices(id) ON DELETE SET NULL,
    channel varchar(16) NOT NULL CHECK (channel IN ('realtime','desktop_queue')),
    status varchar(16) NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','sent','acknowledged','failed','expired')),
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    next_attempt_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    sent_at timestamptz,
    acknowledged_at timestamptz,
    last_error_code varchar(80),
    idempotency_key varchar(200) NOT NULL,
    CONSTRAINT uq_notification_delivery_key UNIQUE (idempotency_key)
);
```

**Индексы:**

- `CREATE INDEX ix_notification_deliveries_due ON notify.notification_deliveries (status, next_attempt_at) WHERE status IN ('pending','failed');`

## 6A.59. `governance.audit_entries`

**Назначение:** Append-only технический аудит.

```sql
CREATE TABLE governance.audit_entries (
    id uuid NOT NULL,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    actor_user_id uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    actor_session_id uuid REFERENCES iam.sessions(id) ON DELETE SET NULL,
    action_code varchar(100) NOT NULL,
    object_id uuid REFERENCES core.objects(id) ON DELETE SET NULL,
    object_type varchar(40),
    outcome varchar(12) NOT NULL CHECK (outcome IN ('success','denied','failure')),
    reason_code varchar(100),
    correlation_id uuid NOT NULL,
    request_id uuid,
    client_ip inet,
    device_id uuid REFERENCES iam.devices(id) ON DELETE SET NULL,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    old_values jsonb,
    new_values jsonb,
    redaction_level varchar(16) NOT NULL DEFAULT 'standard' CHECK (redaction_level IN ('standard','sensitive','fully_redacted')),
    PRIMARY KEY (occurred_at, id)
) PARTITION BY RANGE (occurred_at);
```

**Индексы:**

- `CREATE INDEX ix_audit_org_time ON governance.audit_entries (organization_id, occurred_at DESC, id);`
- `CREATE INDEX ix_audit_object_time ON governance.audit_entries (object_id, occurred_at DESC, id) WHERE object_id IS NOT NULL;`
- `CREATE INDEX ix_audit_actor_time ON governance.audit_entries (actor_user_id, occurred_at DESC, id) WHERE actor_user_id IS NOT NULL;`
- `CREATE INDEX ix_audit_correlation ON governance.audit_entries (correlation_id);`

## 6A.60. `governance.audit_entries_default`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE governance.audit_entries_default PARTITION OF governance.audit_entries DEFAULT;
```

## 6A.61. `governance.object_history`

**Назначение:** Версионная история объектов.

```sql
CREATE TABLE governance.object_history (
    id uuid NOT NULL,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    object_id uuid NOT NULL REFERENCES core.objects(id) ON DELETE RESTRICT,
    object_type varchar(40) NOT NULL,
    object_version bigint NOT NULL CHECK (object_version > 0),
    changed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    changed_by uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    change_type varchar(24) NOT NULL CHECK (change_type IN ('created','updated','state_changed','archived','restored','trashed','purged')),
    changed_fields text[] NOT NULL DEFAULT '{}',
    json_patch jsonb,
    snapshot jsonb,
    correlation_id uuid NOT NULL,
    CONSTRAINT ck_object_history_payload CHECK (json_patch IS NOT NULL OR snapshot IS NOT NULL),
    PRIMARY KEY (changed_at, id)
) PARTITION BY RANGE (changed_at);
```

**Индексы:**

- `CREATE INDEX ix_object_history_object_version ON governance.object_history (object_id, object_version DESC);`
- `CREATE INDEX ix_object_history_org_time ON governance.object_history (organization_id, changed_at DESC);`

## 6A.62. `governance.object_history_default`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE governance.object_history_default PARTITION OF governance.object_history DEFAULT;
```

## 6A.63. `governance.domain_events`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE governance.domain_events (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    aggregate_id uuid NOT NULL,
    aggregate_type varchar(40) NOT NULL,
    aggregate_version bigint NOT NULL,
    event_type varchar(100) NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    actor_user_id uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    correlation_id uuid NOT NULL,
    causation_id uuid,
    idempotency_key varchar(200) NOT NULL,
    payload jsonb NOT NULL,
    schema_version smallint NOT NULL DEFAULT 1 CHECK (schema_version > 0),
    CONSTRAINT uq_domain_event_idempotency UNIQUE (idempotency_key),
    CONSTRAINT uq_domain_event_aggregate_version UNIQUE (aggregate_id, aggregate_version, event_type)
);
```

**Индексы:**

- `CREATE INDEX ix_domain_events_aggregate ON governance.domain_events (aggregate_id, aggregate_version);`
- `CREATE INDEX ix_domain_events_type_time ON governance.domain_events (organization_id, event_type, occurred_at DESC);`

## 6A.64. `governance.outbox_messages`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE governance.outbox_messages (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    domain_event_id uuid NOT NULL REFERENCES governance.domain_events(id) ON DELETE CASCADE,
    destination varchar(40) NOT NULL CHECK (destination IN ('realtime','background','search','notification','sync')),
    message_type varchar(100) NOT NULL,
    payload jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    available_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    status varchar(16) NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','processing','published','failed','dead_letter')),
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    locked_by text,
    locked_at timestamptz,
    published_at timestamptz,
    last_error_code varchar(100),
    last_error_detail text
);
```

**Индексы:**

- `CREATE INDEX ix_outbox_claim ON governance.outbox_messages (status, available_at, created_at) WHERE status IN ('pending','failed');`
- `CREATE INDEX ix_outbox_event ON governance.outbox_messages (domain_event_id);`

## 6A.65. `sync.change_feed`

**Назначение:** Incremental sync cursor stream.

```sql
CREATE TABLE sync.change_feed (
    sequence bigint PRIMARY KEY DEFAULT nextval('sync.change_sequence'),
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    object_id uuid NOT NULL,
    object_type varchar(40) NOT NULL,
    operation varchar(16) NOT NULL CHECK (operation IN ('upsert','tombstone','scope_revoke')),
    object_version bigint NOT NULL,
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    permission_scope_version bigint NOT NULL,
    changed_fields text[] NOT NULL DEFAULT '{}',
    payload_hint jsonb NOT NULL DEFAULT '{}'::jsonb,
    correlation_id uuid NOT NULL
);
```

**Индексы:**

- `CREATE INDEX ix_change_feed_org_sequence ON sync.change_feed (organization_id, sequence);`
- `CREATE INDEX ix_change_feed_object ON sync.change_feed (organization_id, object_id, sequence DESC);`
- `CREATE INDEX ix_change_feed_time ON sync.change_feed (occurred_at);`

## 6A.66. `sync.client_sync_states`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE sync.client_sync_states (
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE CASCADE,
    device_id uuid NOT NULL REFERENCES iam.devices(id) ON DELETE CASCADE,
    last_acknowledged_sequence bigint NOT NULL DEFAULT 0 CHECK (last_acknowledged_sequence >= 0),
    scope_version bigint NOT NULL DEFAULT 1 CHECK (scope_version > 0),
    last_full_sync_at timestamptz,
    last_incremental_sync_at timestamptz,
    cache_schema_version integer NOT NULL DEFAULT 1 CHECK (cache_schema_version > 0),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (user_account_id, device_id)
);
```

## 6A.67. `governance.trash_entries`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE governance.trash_entries (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    object_id uuid NOT NULL REFERENCES core.objects(id) ON DELETE CASCADE,
    object_type varchar(40) NOT NULL,
    deleted_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    deleted_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    purge_after timestamptz NOT NULL,
    original_parent_object_id uuid REFERENCES core.objects(id) ON DELETE SET NULL,
    original_sort_order numeric(20,10),
    deletion_reason text,
    status varchar(20) NOT NULL DEFAULT 'retained' CHECK (status IN ('retained','restored','purged','blocked_by_hold')),
    restored_by uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    restored_at timestamptz,
    purged_at timestamptz,
    CONSTRAINT ck_trash_purge_after CHECK (purge_after > deleted_at)
);
```

**Индексы:**

- `CREATE INDEX ix_trash_org_retention ON governance.trash_entries (organization_id, status, purge_after) WHERE status = 'retained';`
- `CREATE INDEX ix_trash_object ON governance.trash_entries (object_id, deleted_at DESC);`
- `CREATE UNIQUE INDEX uq_trash_object_retained ON governance.trash_entries (object_id) WHERE status = 'retained';`

## 6A.68. `governance.archive_entries`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE governance.archive_entries (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    object_id uuid NOT NULL REFERENCES core.objects(id) ON DELETE CASCADE,
    object_type varchar(40) NOT NULL,
    archived_by uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    archived_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    reason text,
    status varchar(16) NOT NULL DEFAULT 'archived' CHECK (status IN ('archived','restored')),
    restored_by uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    restored_at timestamptz,
);
```

**Индексы:**

- `CREATE INDEX ix_archive_org_time ON governance.archive_entries (organization_id, status, archived_at DESC);`
- `CREATE UNIQUE INDEX uq_archive_object_active ON governance.archive_entries (object_id) WHERE status = 'archived';`

## 6A.69. `search.search_documents`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE search.search_documents (
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE CASCADE,
    object_id uuid NOT NULL REFERENCES core.objects(id) ON DELETE CASCADE,
    object_type varchar(40) NOT NULL,
    title text NOT NULL,
    body text,
    tags_text text,
    path_text text,
    search_vector tsvector GENERATED ALWAYS AS (
        setweight(to_tsvector('russian', coalesce(title,'')), 'A') ||
        setweight(to_tsvector('russian', coalesce(body,'')), 'B') ||
        setweight(to_tsvector('simple', coalesce(tags_text,'')), 'B') ||
        setweight(to_tsvector('simple', coalesce(path_text,'')), 'C')
    ) STORED,
    permission_hints jsonb NOT NULL DEFAULT '{}'::jsonb,
    object_version bigint NOT NULL,
    indexed_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (organization_id, object_id)
);
```

**Индексы:**

- `CREATE INDEX ix_search_documents_vector ON search.search_documents USING gin (search_vector);`
- `CREATE INDEX ix_search_documents_title_trgm ON search.search_documents USING gin (title gin_trgm_ops);`
- `CREATE INDEX ix_search_documents_path_trgm ON search.search_documents USING gin (path_text gin_trgm_ops);`
- `CREATE INDEX ix_search_documents_type ON search.search_documents (organization_id, object_type, indexed_at DESC);`

## 6A.70. `ops.background_jobs`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE ops.background_jobs (
    id uuid PRIMARY KEY,
    organization_id uuid REFERENCES core.organizations(id) ON DELETE CASCADE,
    job_code citext NOT NULL,
    schedule_kind varchar(16) NOT NULL CHECK (schedule_kind IN ('cron','interval','event','continuous')),
    schedule_expression text,
    is_enabled boolean NOT NULL DEFAULT true,
    concurrency_key varchar(200),
    max_parallelism smallint NOT NULL DEFAULT 1 CHECK (max_parallelism BETWEEN 1 AND 32),
    max_attempts smallint NOT NULL DEFAULT 5 CHECK (max_attempts BETWEEN 1 AND 50),
    timeout_seconds integer NOT NULL DEFAULT 300 CHECK (timeout_seconds BETWEEN 1 AND 86400),
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_background_jobs_code UNIQUE NULLS NOT DISTINCT (organization_id, job_code)
);
```

## 6A.71. `ops.background_job_runs`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE ops.background_job_runs (
    id uuid PRIMARY KEY,
    job_id uuid NOT NULL REFERENCES ops.background_jobs(id) ON DELETE CASCADE,
    organization_id uuid REFERENCES core.organizations(id) ON DELETE CASCADE,
    trigger_type varchar(16) NOT NULL CHECK (trigger_type IN ('schedule','event','manual','retry')),
    idempotency_key varchar(200) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'queued' CHECK (status IN ('queued','running','succeeded','failed','dead_letter','cancelled')),
    scheduled_at timestamptz NOT NULL,
    started_at timestamptz,
    finished_at timestamptz,
    worker_id text,
    attempt integer NOT NULL DEFAULT 0 CHECK (attempt >= 0),
    input_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
    result_payload jsonb,
    error_code varchar(100),
    error_detail text,
    CONSTRAINT uq_background_job_run_key UNIQUE (idempotency_key)
);
```

**Индексы:**

- `CREATE INDEX ix_background_job_runs_claim ON ops.background_job_runs (status, scheduled_at) WHERE status IN ('queued','failed');`
- `CREATE INDEX ix_background_job_runs_job_time ON ops.background_job_runs (job_id, scheduled_at DESC);`

## 6A.72. `ops.backup_runs`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE ops.backup_runs (
    id uuid PRIMARY KEY,
    organization_id uuid REFERENCES core.organizations(id) ON DELETE SET NULL,
    backup_type varchar(20) NOT NULL CHECK (backup_type IN ('base','incremental','wal_archive','config','restore_test')),
    started_at timestamptz NOT NULL,
    finished_at timestamptz,
    status varchar(20) NOT NULL CHECK (status IN ('running','succeeded','failed','cancelled')),
    repository_uri text NOT NULL,
    encrypted boolean NOT NULL DEFAULT true,
    size_bytes bigint CHECK (size_bytes IS NULL OR size_bytes >= 0),
    checksum text,
    rpo_seconds integer,
    restore_tested_at timestamptz,
    error_code varchar(100),
    sanitized_error text,
    initiated_by uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
```

**Индексы:**

- `CREATE INDEX ix_backup_runs_time ON ops.backup_runs (started_at DESC, status);`

## 6A.73. `ops.feature_flags`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE ops.feature_flags (
    id uuid PRIMARY KEY,
    organization_id uuid REFERENCES core.organizations(id) ON DELETE CASCADE,
    flag_key citext NOT NULL,
    enabled boolean NOT NULL DEFAULT false,
    minimum_client_version varchar(32),
    configuration jsonb NOT NULL DEFAULT '{}'::jsonb,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    updated_by uuid REFERENCES iam.user_accounts(id) ON DELETE SET NULL,
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_feature_flags_key UNIQUE NULLS NOT DISTINCT (organization_id, flag_key)
);
```

## 6A.74. `ops.server_capabilities`

**Назначение:** Техническая таблица соответствующего bounded context; назначение следует из имени и FK.

```sql
CREATE TABLE ops.server_capabilities (
    capability_key citext PRIMARY KEY,
    enabled boolean NOT NULL DEFAULT true,
    minimum_api_version varchar(16) NOT NULL DEFAULT 'v1',
    configuration jsonb NOT NULL DEFAULT '{}'::jsonb,
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
```

## 6A.X. Коррекции независимого аудита

```sql
-- Organizer Stage 2 corrections found by independent audit.
-- Apply after 001_initial_schema.sql. PostgreSQL 16+.
BEGIN;

-- User-level settings required by concept §23 and API /settings/me.
CREATE TABLE IF NOT EXISTS org.user_settings (
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
    do_not_disturb_start time,
    do_not_disturb_end time,
    autostart_enabled boolean NOT NULL DEFAULT true,
    sounds_enabled boolean NOT NULL DEFAULT true,
    allow_local_paths boolean NOT NULL DEFAULT true,
    confirm_catalog_delete boolean NOT NULL DEFAULT true,
    missing_file_behavior varchar(24) NOT NULL DEFAULT 'show_actions' CHECK (missing_file_behavior IN ('show_actions','keep_inactive','prompt_relink')),
    custom_preferences jsonb NOT NULL DEFAULT '{}'::jsonb,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_user_settings_workday CHECK (workday_end > workday_start),
    CONSTRAINT ck_user_settings_weekend CHECK (weekend_days <@ ARRAY[1,2,3,4,5,6,7]::smallint[])
);
CREATE INDEX IF NOT EXISTS ix_user_settings_org ON org.user_settings (organization_id, user_account_id);

-- Canonical permission catalog synchronized with the API catalog.
INSERT INTO iam.permissions (id, code, resource, action, description, is_sensitive) VALUES
('20000000-0000-7000-8000-000000000001','Audit.ReadAll','Audit','ReadAll','Просмотр общего аудита',true),
('20000000-0000-7000-8000-000000000002','Authorization.Explain','Authorization','Explain','Диагностика вычисленного решения доступа',true),
('20000000-0000-7000-8000-000000000003','Backup.Execute','Backup','Execute','Ручной запуск резервного копирования',true),
('20000000-0000-7000-8000-000000000004','Backup.Read','Backup','Read','Просмотр состояния резервного копирования',true),
('20000000-0000-7000-8000-000000000005','Backup.Restore','Backup','Restore','Запрос контролируемого восстановления',true),
('20000000-0000-7000-8000-000000000006','Backup.RestoreTest','Backup','RestoreTest','Запуск тестового восстановления',true),
('20000000-0000-7000-8000-000000000007','Calendar.Read','Calendar','Read','Просмотр календаря в доступной области',false),
('20000000-0000-7000-8000-000000000008','CalendarEvent.Create','CalendarEvent','Create','Создание календарного события',false),
('20000000-0000-7000-8000-000000000009','CalendarEvent.Delete','CalendarEvent','Delete','Удаление календарного события',false),
('20000000-0000-7000-8000-000000000010','CalendarEvent.Respond','CalendarEvent','Respond','Ответ участника на приглашение',false),
('20000000-0000-7000-8000-000000000011','CalendarEvent.Update','CalendarEvent','Update','Изменение календарного события',false),
('20000000-0000-7000-8000-000000000012','Comment.Create','Comment','Create','Добавление комментария',false),
('20000000-0000-7000-8000-000000000013','Comment.DeleteOwnOrModerate','Comment','Delete','Удаление своего комментария; модерация отдельно',false),
('20000000-0000-7000-8000-000000000014','Comment.Moderate','Comment','Moderate','Модерация и восстановление комментариев',true),
('20000000-0000-7000-8000-000000000015','Comment.Read','Comment','Read','Просмотр комментариев доступного объекта',false),
('20000000-0000-7000-8000-000000000016','Comment.UpdateOwnOrModerate','Comment','Update','Редактирование своего комментария; модерация отдельно',false),
('20000000-0000-7000-8000-000000000017','Contact.Create','Contact','Create','Создание контакта или компании',false),
('20000000-0000-7000-8000-000000000018','Contact.Delete','Contact','Delete','Помещение контакта в корзину',false),
('20000000-0000-7000-8000-000000000019','Contact.Read','Contact','Read','Просмотр доступных контактов и компаний',false),
('20000000-0000-7000-8000-000000000020','Contact.Restore','Contact','Restore','Восстановление контакта',false),
('20000000-0000-7000-8000-000000000021','Contact.Update','Contact','Update','Изменение контакта или компании',false),
('20000000-0000-7000-8000-000000000022','Department.Manage','Department','Manage','Создание и изменение отделов',true),
('20000000-0000-7000-8000-000000000023','Department.Read','Department','Read','Просмотр отделов',false),
('20000000-0000-7000-8000-000000000024','Device.ReadOwnOrAll','Device','Read','Свои устройства; все устройства для администратора',true),
('20000000-0000-7000-8000-000000000025','Device.Revoke','Device','Revoke','Отзыв устройства или сессии',true),
('20000000-0000-7000-8000-000000000026','Device.UpdateOwnOrAll','Device','Update','Переименование своего устройства или административное изменение',true),
('20000000-0000-7000-8000-000000000027','FileCatalog.Create','FileCatalog','Create','Создание элемента каталога',false),
('20000000-0000-7000-8000-000000000028','FileCatalog.Delete','FileCatalog','Delete','Помещение записи каталога в корзину',false),
('20000000-0000-7000-8000-000000000029','FileCatalog.Read','FileCatalog','Read','Просмотр каталога и метаданных',false),
('20000000-0000-7000-8000-000000000030','FileCatalog.Restore','FileCatalog','Restore','Восстановление записи каталога',false),
('20000000-0000-7000-8000-000000000031','FileCatalog.Update','FileCatalog','Update','Изменение метаданных и виртуальной структуры',false),
('20000000-0000-7000-8000-000000000032','FileLocation.Update','FileLocation','Update','Добавление и перепривязка физических путей',true),
('20000000-0000-7000-8000-000000000033','FileReference.Open','FileReference','Open','Получение разрешённых путей для открытия',false),
('20000000-0000-7000-8000-000000000034','History.Read','History','Read','Просмотр истории доступного объекта',false),
('20000000-0000-7000-8000-000000000035','Inbox.ManageOwn','Inbox','ManageOwn','Управление собственными входящими',false),
('20000000-0000-7000-8000-000000000036','Inbox.ReadOwn','Inbox','ReadOwn','Просмотр собственных входящих',false),
('20000000-0000-7000-8000-000000000037','Interaction.Create','Interaction','Create','Добавление взаимодействия',false),
('20000000-0000-7000-8000-000000000038','Interaction.Update','Interaction','Update','Изменение взаимодействия',false),
('20000000-0000-7000-8000-000000000039','NetworkResource.Manage','NetworkResource','Manage','Управление разрешёнными сетевыми корнями',true),
('20000000-0000-7000-8000-000000000040','Notification.ManageOwn','Notification','ManageOwn','Чтение, скрытие и отсрочка собственных уведомлений',false),
('20000000-0000-7000-8000-000000000041','Notification.ReadOwn','Notification','ReadOwn','Просмотр собственных уведомлений',false),
('20000000-0000-7000-8000-000000000042','ObjectLink.Create','ObjectLink','Create','Создание связи между доступными объектами',false),
('20000000-0000-7000-8000-000000000043','ObjectLink.Delete','ObjectLink','Delete','Удаление связи между доступными объектами',false),
('20000000-0000-7000-8000-000000000044','ObjectLink.Read','ObjectLink','Read','Просмотр связей доступного объекта',false),
('20000000-0000-7000-8000-000000000045','Organization.Read','Organization','Read','Просмотр организации',false),
('20000000-0000-7000-8000-000000000046','Organization.Update','Organization','Update','Изменение настроек организации',true),
('20000000-0000-7000-8000-000000000047','Project.Archive','Project','Archive','Архивирование проекта',false),
('20000000-0000-7000-8000-000000000048','Project.Create','Project','Create','Создание проекта',false),
('20000000-0000-7000-8000-000000000049','Project.Delete','Project','Delete','Помещение проекта в корзину',true),
('20000000-0000-7000-8000-000000000050','Project.ManageMembers','Project','ManageMembers','Управление участниками и проектными ролями',true),
('20000000-0000-7000-8000-000000000051','Project.Read','Project','Read','Просмотр проекта по области доступа',false),
('20000000-0000-7000-8000-000000000052','Project.Restore','Project','Restore','Восстановление проекта',true),
('20000000-0000-7000-8000-000000000053','Project.TransferOwnership','Project','TransferOwnership','Разрешение Project.TransferOwnership',false),
('20000000-0000-7000-8000-000000000054','Project.Update','Project','Update','Изменение проекта',false),
('20000000-0000-7000-8000-000000000055','Reminder.ManageOwn','Reminder','ManageOwn','Управление собственными напоминаниями',false),
('20000000-0000-7000-8000-000000000056','Role.Manage','Role','Manage','Изменение ролей и разрешений',true),
('20000000-0000-7000-8000-000000000057','Role.Read','Role','Read','Просмотр ролей и разрешений',true),
('20000000-0000-7000-8000-000000000058','Search.Use','Search','Use','Глобальный поиск',false),
('20000000-0000-7000-8000-000000000059','SecurityAudit.Read','SecurityAudit','Read','Просмотр событий безопасности',true),
('20000000-0000-7000-8000-000000000060','Session.ReadOwnOrAll','Session','Read','Свои сессии; чужие только с административным scope',true),
('20000000-0000-7000-8000-000000000061','Session.RevokeOwnOrAll','Session','Revoke','Отзыв своей сессии или чужой при административном праве',true),
('20000000-0000-7000-8000-000000000062','Settings.ReadOwn','Settings','ReadOwn','Разрешение Settings.ReadOwn',false),
('20000000-0000-7000-8000-000000000063','Settings.UpdateOwn','Settings','UpdateOwn','Изменение пользовательских настроек',false),
('20000000-0000-7000-8000-000000000064','Sync.Read','Sync','Read','Bootstrap и incremental sync собственных доступных данных',true),
('20000000-0000-7000-8000-000000000065','System.Configure','System','Configure','Изменение разрешённых системных параметров',true),
('20000000-0000-7000-8000-000000000066','System.HealthRead','System','HealthRead','Просмотр состояния сервера',true),
('20000000-0000-7000-8000-000000000067','System.JobRun','System','JobRun','Разрешение System.JobRun',false),
('20000000-0000-7000-8000-000000000068','Tag.Assign','Tag','Assign','Назначение тегов доступному объекту',false),
('20000000-0000-7000-8000-000000000069','Tag.Manage','Tag','Manage','Управление тегами',false),
('20000000-0000-7000-8000-000000000070','Tag.Read','Tag','Read','Просмотр справочника тегов',false),
('20000000-0000-7000-8000-000000000071','Task.Archive','Task','Archive','Архивирование задачи',false),
('20000000-0000-7000-8000-000000000072','Task.Assign','Task','Assign','Назначение исполнителей',false),
('20000000-0000-7000-8000-000000000073','Task.ChangeStatus','Task','ChangeStatus','Изменение статуса задачи',false),
('20000000-0000-7000-8000-000000000074','Task.Create','Task','Create','Создание задачи',false),
('20000000-0000-7000-8000-000000000075','Task.Delete','Task','Delete','Помещение задачи в корзину',false),
('20000000-0000-7000-8000-000000000076','Task.ManageRecurrence','Task','ManageRecurrence','Изменение серии повторений',false),
('20000000-0000-7000-8000-000000000077','Task.ManageWatchers','Task','ManageWatchers','Управление наблюдателями задачи',false),
('20000000-0000-7000-8000-000000000078','Task.Read','Task','Read','Просмотр задач по области доступа',false),
('20000000-0000-7000-8000-000000000079','Task.Restore','Task','Restore','Восстановление задачи',false),
('20000000-0000-7000-8000-000000000080','Task.Update','Task','Update','Редактирование задачи',false),
('20000000-0000-7000-8000-000000000081','Trash.Purge','Trash','Purge','Физическая очистка метаданных после retention',true),
('20000000-0000-7000-8000-000000000082','Trash.Read','Trash','Read','Просмотр доступной корзины',false),
('20000000-0000-7000-8000-000000000083','User.Block','User','Block','Блокировка и разблокировка учётной записи',true),
('20000000-0000-7000-8000-000000000084','User.Create','User','Create','Создание учётной записи и профиля',true),
('20000000-0000-7000-8000-000000000085','User.ManageRoles','User','ManageRoles','Назначение системных ролей',true),
('20000000-0000-7000-8000-000000000086','User.Read','User','Read','Просмотр доступных сотрудников',false),
('20000000-0000-7000-8000-000000000087','User.ResetPassword','User','ResetPassword','Административный сброс пароля',true),
('20000000-0000-7000-8000-000000000088','User.Update','User','Update','Редактирование профиля сотрудника',true)
ON CONFLICT (code) DO UPDATE SET
  resource=EXCLUDED.resource,
  action=EXCLUDED.action,
  description=EXCLUDED.description,
  is_sensitive=EXCLUDED.is_sensitive;

-- A user account is deactivated/blocked, never soft-deleted by business API.
COMMENT ON TABLE iam.user_accounts IS 'Security account. Business lifecycle uses pending_activation/active/blocked/deactivated; no trash/delete API.';

COMMIT;

```
