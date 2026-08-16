CREATE SCHEMA IF NOT EXISTS org;
CREATE SCHEMA IF NOT EXISTS iam;
CREATE SCHEMA IF NOT EXISTS governance;

CREATE TABLE org.employee_profiles (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    first_name text NOT NULL,
    last_name text NOT NULL,
    middle_name text,
    display_name text NOT NULL,
    job_title text,
    work_email citext,
    employment_status varchar(20) NOT NULL DEFAULT 'active',
    preferred_time_zone text NOT NULL,
    locale varchar(16) NOT NULL DEFAULT 'ru-RU',
    CONSTRAINT uq_employee_profiles_org_id UNIQUE (organization_id, id),
    CONSTRAINT ck_employee_profiles_status CHECK (employment_status IN ('active', 'suspended', 'terminated')),
    CONSTRAINT ck_employee_profiles_first_name CHECK (length(btrim(first_name)) BETWEEN 1 AND 100),
    CONSTRAINT ck_employee_profiles_last_name CHECK (length(btrim(last_name)) BETWEEN 1 AND 100),
    CONSTRAINT ck_employee_profiles_display_name CHECK (length(btrim(display_name)) BETWEEN 1 AND 200),
    CONSTRAINT ck_employee_profiles_timezone CHECK (length(btrim(preferred_time_zone)) BETWEEN 1 AND 64)
);

CREATE TABLE iam.user_accounts (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    employee_profile_id uuid NOT NULL REFERENCES org.employee_profiles(id) ON DELETE RESTRICT,
    login citext NOT NULL,
    password_hash text NOT NULL,
    password_algorithm varchar(32) NOT NULL DEFAULT 'argon2id',
    password_parameters jsonb NOT NULL,
    credential_version bigint NOT NULL DEFAULT 1,
    account_status varchar(20) NOT NULL DEFAULT 'active',
    must_change_password boolean NOT NULL DEFAULT true,
    failed_login_count integer NOT NULL DEFAULT 0,
    locked_until timestamptz,
    last_login_at timestamptz,
    last_activity_at timestamptz,
    CONSTRAINT uq_user_accounts_org_id UNIQUE (organization_id, id),
    CONSTRAINT uq_user_accounts_org_login UNIQUE (organization_id, login),
    CONSTRAINT uq_user_accounts_employee UNIQUE (employee_profile_id),
    CONSTRAINT ck_user_accounts_algorithm CHECK (password_algorithm = 'argon2id'),
    CONSTRAINT ck_user_accounts_credential_version CHECK (credential_version > 0),
    CONSTRAINT ck_user_accounts_status CHECK (account_status IN ('pending', 'active', 'blocked', 'deactivated')),
    CONSTRAINT ck_user_accounts_failed_login_count CHECK (failed_login_count >= 0),
    CONSTRAINT ck_user_accounts_hash CHECK (length(password_hash) BETWEEN 32 AND 1024)
);

CREATE TABLE iam.password_history (
    id uuid PRIMARY KEY,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    password_hash text NOT NULL,
    password_algorithm varchar(32) NOT NULL,
    password_parameters jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_password_history_algorithm CHECK (password_algorithm = 'argon2id')
);

CREATE TABLE iam.devices (
    id uuid PRIMARY KEY REFERENCES core.objects(id) ON DELETE RESTRICT,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    device_fingerprint_hash text NOT NULL,
    display_name text,
    first_seen_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    last_seen_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    revoked_at timestamptz,
    CONSTRAINT uq_devices_org_id UNIQUE (organization_id, id),
    CONSTRAINT uq_devices_user_fingerprint UNIQUE (user_account_id, device_fingerprint_hash),
    CONSTRAINT ck_devices_fingerprint CHECK (length(device_fingerprint_hash) BETWEEN 32 AND 256)
);

CREATE TABLE iam.sessions (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    device_id uuid REFERENCES iam.devices(id) ON DELETE SET NULL,
    credential_version bigint NOT NULL,
    authorization_scope_version bigint NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    last_seen_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    idle_expires_at timestamptz NOT NULL,
    absolute_expires_at timestamptz NOT NULL,
    revoked_at timestamptz,
    revoke_reason varchar(64),
    CONSTRAINT ck_sessions_versions CHECK (credential_version > 0 AND authorization_scope_version > 0),
    CONSTRAINT ck_sessions_expiry CHECK (idle_expires_at <= absolute_expires_at)
);

CREATE TABLE iam.refresh_tokens (
    id uuid PRIMARY KEY,
    session_id uuid NOT NULL REFERENCES iam.sessions(id) ON DELETE RESTRICT,
    token_hash text NOT NULL,
    issued_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    consumed_at timestamptz,
    replaced_by_id uuid,
    revoked_at timestamptz,
    CONSTRAINT uq_refresh_tokens_hash UNIQUE (token_hash),
    CONSTRAINT ck_refresh_tokens_hash CHECK (length(token_hash) BETWEEN 32 AND 256)
);

CREATE TABLE iam.permissions (
    code varchar(128) PRIMARY KEY,
    description text NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    CONSTRAINT ck_permissions_code CHECK (code ~ '^[a-z][a-z0-9]*(\\.[a-z][a-z0-9]*)+$')
);

CREATE TABLE iam.roles (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    code varchar(128) NOT NULL,
    display_name text NOT NULL,
    is_system boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT uq_roles_org_code UNIQUE (organization_id, code),
    CONSTRAINT ck_roles_code CHECK (code ~ '^[a-z][a-z0-9_]{1,127}$')
);

CREATE TABLE iam.role_permissions (
    role_id uuid NOT NULL REFERENCES iam.roles(id) ON DELETE RESTRICT,
    permission_code varchar(128) NOT NULL REFERENCES iam.permissions(code) ON DELETE RESTRICT,
    PRIMARY KEY (role_id, permission_code)
);

CREATE TABLE iam.user_roles (
    user_account_id uuid NOT NULL REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    role_id uuid NOT NULL REFERENCES iam.roles(id) ON DELETE RESTRICT,
    granted_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    granted_by uuid REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    PRIMARY KEY (user_account_id, role_id)
);

CREATE TABLE iam.authorization_scope_versions (
    user_account_id uuid PRIMARY KEY REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    version bigint NOT NULL DEFAULT 1,
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT ck_authorization_scope_versions_version CHECK (version > 0)
);

INSERT INTO iam.permissions (code, description) VALUES
    ('identity.account.manage', 'Manage employee accounts and credentials.'),
    ('identity.role.manage', 'Manage organization roles and permissions.'),
    ('audit.entry.read', 'Read security audit evidence.'),
    ('task.manage', 'Manage organization tasks.'),
    ('organization.manage', 'Manage organization settings.')
ON CONFLICT (code) DO NOTHING;

CREATE TABLE governance.audit_entries (
    id uuid NOT NULL,
    organization_id uuid NOT NULL REFERENCES core.organizations(id) ON DELETE RESTRICT,
    occurred_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    actor_user_id uuid REFERENCES iam.user_accounts(id) ON DELETE RESTRICT,
    actor_session_id uuid REFERENCES iam.sessions(id) ON DELETE RESTRICT,
    action_code varchar(128) NOT NULL,
    object_id uuid,
    object_type varchar(40),
    outcome varchar(16) NOT NULL,
    reason_code varchar(128),
    correlation_id uuid NOT NULL,
    request_id uuid NOT NULL,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    old_state jsonb,
    new_state jsonb,
    redaction_level varchar(20) NOT NULL DEFAULT 'standard',
    PRIMARY KEY (occurred_at, id),
    CONSTRAINT ck_audit_entries_outcome CHECK (outcome IN ('success', 'denied', 'failure')),
    CONSTRAINT ck_audit_entries_action CHECK (length(btrim(action_code)) BETWEEN 1 AND 128),
    CONSTRAINT ck_audit_entries_redaction CHECK (redaction_level IN ('standard', 'restricted'))
) PARTITION BY RANGE (occurred_at);

CREATE TABLE governance.audit_entries_default PARTITION OF governance.audit_entries DEFAULT;
CREATE INDEX ix_audit_entries_org_occurred ON governance.audit_entries (organization_id, occurred_at DESC);
CREATE INDEX ix_audit_entries_actor_occurred ON governance.audit_entries (actor_user_id, occurred_at DESC) WHERE actor_user_id IS NOT NULL;

CREATE FUNCTION governance.reject_audit_mutation() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'APPEND_ONLY_AUDIT_ENTRIES' USING ERRCODE = '42501';
END;
$$;

CREATE TRIGGER trg_audit_entries_append_only
BEFORE UPDATE OR DELETE ON governance.audit_entries
FOR EACH ROW EXECUTE FUNCTION governance.reject_audit_mutation();

CREATE INDEX ix_user_accounts_org_login ON iam.user_accounts (organization_id, login);
CREATE INDEX ix_sessions_active_user ON iam.sessions (user_account_id, absolute_expires_at) WHERE revoked_at IS NULL;
CREATE INDEX ix_refresh_tokens_session ON iam.refresh_tokens (session_id, expires_at);
