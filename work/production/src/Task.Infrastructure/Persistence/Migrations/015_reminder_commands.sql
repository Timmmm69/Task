-- Add concurrency to existing occurrence rows without rewriting migration 14.
ALTER TABLE calendar.reminder_occurrences ADD COLUMN version bigint NOT NULL DEFAULT 1 CHECK (version > 0);
CREATE FUNCTION calendar.bump_reminder_occurrence_version() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    NEW.version := OLD.version + 1;
    RETURN NEW;
END;
$$;
CREATE TRIGGER reminder_occurrence_version BEFORE UPDATE ON calendar.reminder_occurrences
FOR EACH ROW EXECUTE FUNCTION calendar.bump_reminder_occurrence_version();

INSERT INTO iam.permissions(code,description) VALUES
('reminder.manageown','Manage own reminders for currently accessible tasks and calendar events.')
ON CONFLICT(code) DO NOTHING;
INSERT INTO iam.system_role_templates(role_code,permission_code)
SELECT role_code,'reminder.manageown' FROM iam.system_role_templates
WHERE permission_code='notification.manageown' ON CONFLICT DO NOTHING;
INSERT INTO iam.role_permissions(role_id,permission_code,effect)
SELECT role.id,'reminder.manageown','grant' FROM iam.roles role
JOIN iam.system_role_templates template ON template.role_code=role.code AND template.permission_code='reminder.manageown'
ON CONFLICT DO NOTHING;

ALTER TABLE governance.domain_events DROP CONSTRAINT ck_domain_event_aggregate;
ALTER TABLE governance.domain_events ADD CONSTRAINT ck_domain_event_aggregate
CHECK (aggregate_type IN ('task','calendar_event','recurrence_series','project','contact','company',
 'catalog_item','network_resource','notification','interaction','user-settings','organization-settings',
 'preferences','user_account','device','reminder'));
