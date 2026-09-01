-- Add the canonical CalendarEvent write capabilities. Existing task managers
-- retain calendar write access while roles can subsequently be narrowed to an
-- individual create, update or delete capability.

INSERT INTO iam.permissions (code, description) VALUES
    ('calendarevent.create', 'Create organization calendar events.'),
    ('calendarevent.update', 'Update and archive organization calendar events.'),
    ('calendarevent.delete', 'Move organization calendar events to or from trash.')
ON CONFLICT (code) DO NOTHING;

INSERT INTO iam.role_permissions (role_id, permission_code, effect)
SELECT rp.role_id, capability.code, rp.effect
FROM iam.role_permissions rp
CROSS JOIN (VALUES
    ('calendarevent.create'),
    ('calendarevent.update'),
    ('calendarevent.delete')
) AS capability(code)
WHERE rp.permission_code = 'task.manage'
ON CONFLICT (role_id, permission_code, effect) DO NOTHING;
