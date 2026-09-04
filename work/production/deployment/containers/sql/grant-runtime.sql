\set ON_ERROR_STOP on

REVOKE ALL ON SCHEMA infrastructure, core, work, org, iam, governance, calendar, projects, crm, files, notify FROM task_runtime;
GRANT USAGE ON SCHEMA infrastructure, core, work, org, iam, governance, calendar, projects, crm, files, notify TO task_runtime;

REVOKE ALL ON TABLE infrastructure.schema_migrations FROM task_runtime;
REVOKE ALL ON TABLE core.organizations FROM task_runtime;
REVOKE ALL ON TABLE core.objects FROM task_runtime;
REVOKE ALL ON TABLE work.tasks FROM task_runtime;
REVOKE ALL ON ALL TABLES IN SCHEMA org, iam, governance, calendar, projects, crm, files, notify FROM task_runtime;
REVOKE ALL ON TABLE core.organization_settings FROM task_runtime;

GRANT SELECT ON TABLE infrastructure.schema_migrations TO task_runtime;
GRANT SELECT ON TABLE core.organizations TO task_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE core.objects TO task_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE work.tasks TO task_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE org.employee_profiles TO task_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE iam.user_accounts, iam.devices, iam.sessions, iam.refresh_tokens, iam.authorization_scope_versions TO task_runtime;
GRANT SELECT, INSERT ON TABLE iam.password_history, iam.user_roles, iam.role_permissions TO task_runtime;
GRANT SELECT ON TABLE iam.permissions, iam.roles TO task_runtime;
GRANT SELECT, INSERT ON TABLE governance.audit_entries TO task_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE iam.idempotency_records TO task_runtime;
GRANT SELECT, INSERT ON TABLE governance.domain_events, governance.outbox_messages TO task_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE calendar.events, calendar.recurrence_series, calendar.recurrence_occurrences TO task_runtime;
GRANT SELECT, INSERT, DELETE ON TABLE calendar.event_user_attendees, calendar.event_contact_attendees TO task_runtime;
GRANT SELECT, INSERT ON TABLE calendar.recurrence_commands TO task_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE projects.projects, crm.contacts, files.catalog_items, notify.notifications TO task_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE core.organization_settings, org.user_settings, notify.notification_preferences TO task_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE governance.archive_entries, governance.trash_entries TO task_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE crm.companies, files.network_resources, projects.members TO task_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE crm.communication_channels, crm.addresses, crm.company_contacts, files.file_locations TO task_runtime;
GRANT SELECT, INSERT ON TABLE iam.product_api_commands TO task_runtime;
GRANT SELECT, INSERT, DELETE ON TABLE core.product_search_snapshots TO task_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE crm.interactions, files.location_checks TO task_runtime;
GRANT SELECT, INSERT, DELETE ON TABLE core.object_links TO task_runtime;

GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE work.task_checklist TO task_runtime;
GRANT SELECT, INSERT ON TABLE work.task_comments TO task_runtime;
GRANT SELECT, INSERT, DELETE ON TABLE work.task_dependencies TO task_runtime;
