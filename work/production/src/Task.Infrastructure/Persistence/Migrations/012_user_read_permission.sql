-- Introduce the canonical User.Read capability while preserving access
-- currently carried by the temporary identity.account.manage bridge.
INSERT INTO iam.permissions (code, description) VALUES
    ('user.read', 'Read organization user accounts and employee profile projections.'),
    ('user.create', 'Create organization user accounts.'),
    ('user.update', 'Update organization user accounts.'),
    ('user.block', 'Block, unblock and deactivate organization user accounts.'),
    ('user.resetpassword', 'Reset organization user account credentials.'),
    ('device.readownorall', 'Read own devices or all devices with administrative scope.'),
    ('device.updateownorall', 'Update own devices or all devices with administrative scope.'),
    ('device.revoke', 'Revoke devices and their sessions.'),
    ('session.readownorall', 'Read own sessions or all sessions with administrative scope.'),
    ('session.revokeownorall', 'Revoke own sessions or all sessions with administrative scope.')
ON CONFLICT (code) DO NOTHING;

INSERT INTO iam.role_permissions (role_id, permission_code, effect)
SELECT rp.role_id, capability.code, rp.effect
FROM iam.role_permissions AS rp
 CROSS JOIN (VALUES
    ('user.read'), ('user.create'), ('user.update'), ('user.block'), ('user.resetpassword'),
    ('device.readownorall'), ('device.updateownorall'), ('device.revoke'),
    ('session.readownorall'), ('session.revokeownorall')) AS capability(code)
WHERE rp.permission_code = 'identity.account.manage'
ON CONFLICT (role_id, permission_code, effect) DO NOTHING;

ALTER TABLE org.employee_profiles ADD COLUMN department_id uuid;
ALTER TABLE iam.user_accounts ADD COLUMN temporary_password_expires_at timestamptz;
ALTER TABLE iam.devices ADD COLUMN platform varchar(16) NOT NULL DEFAULT 'windows';
ALTER TABLE iam.devices ADD COLUMN app_version varchar(32) NOT NULL DEFAULT 'unknown';
ALTER TABLE iam.devices ADD COLUMN os_version varchar(100);

ALTER TABLE iam.devices ADD CONSTRAINT ck_devices_platform
    CHECK (platform IN ('windows', 'linux', 'macos'));
ALTER TABLE iam.devices ADD CONSTRAINT ck_devices_app_version
    CHECK (length(btrim(app_version)) BETWEEN 1 AND 32);

ALTER TABLE governance.domain_events DROP CONSTRAINT ck_domain_event_aggregate;
ALTER TABLE governance.domain_events ADD CONSTRAINT ck_domain_event_aggregate
    CHECK (aggregate_type IN ('task','calendar_event','recurrence_series','project','contact','company',
        'catalog_item','network_resource','notification','interaction','user-settings',
        'organization-settings','preferences','user_account','device'));
