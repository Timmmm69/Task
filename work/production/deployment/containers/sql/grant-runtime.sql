\set ON_ERROR_STOP on

REVOKE ALL ON SCHEMA infrastructure, core, work FROM task_runtime;
GRANT USAGE ON SCHEMA infrastructure, core, work TO task_runtime;

REVOKE ALL ON TABLE infrastructure.schema_migrations FROM task_runtime;
REVOKE ALL ON TABLE core.organizations FROM task_runtime;
REVOKE ALL ON TABLE core.objects FROM task_runtime;
REVOKE ALL ON TABLE work.tasks FROM task_runtime;

GRANT SELECT ON TABLE infrastructure.schema_migrations TO task_runtime;
GRANT SELECT ON TABLE core.organizations TO task_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE core.objects TO task_runtime;
GRANT SELECT, INSERT, UPDATE ON TABLE work.tasks TO task_runtime;
