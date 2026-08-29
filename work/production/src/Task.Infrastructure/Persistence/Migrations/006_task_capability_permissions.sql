-- Split the temporary task.manage bridge into the canonical Task capabilities.
-- Existing roles retain their effective access; new read-only roles can receive task.read alone.

INSERT INTO iam.permissions (code, description) VALUES
    ('task.read', 'Read organization tasks within the allowed scope.'),
    ('task.create', 'Create organization tasks.'),
    ('task.update', 'Update editable task fields.'),
    ('task.changestatus', 'Change task workflow status.')
ON CONFLICT (code) DO NOTHING;

INSERT INTO iam.role_permissions (role_id, permission_code, effect)
SELECT rp.role_id, capability.code, rp.effect
FROM iam.role_permissions rp
CROSS JOIN (VALUES
    ('task.read'),
    ('task.create'),
    ('task.update'),
    ('task.changestatus')
) AS capability(code)
WHERE rp.permission_code = 'task.manage'
ON CONFLICT (role_id, permission_code, effect) DO NOTHING;
