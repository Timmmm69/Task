CREATE ROLE task_backup LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
GRANT CONNECT ON DATABASE postgres TO task_backup;
GRANT pg_monitor, pg_checkpoint TO task_backup;
GRANT EXECUTE ON FUNCTION pg_backup_start(text, boolean) TO task_backup;
GRANT EXECUTE ON FUNCTION pg_backup_stop(boolean) TO task_backup;
GRANT EXECUTE ON FUNCTION pg_switch_wal() TO task_backup;
GRANT EXECUTE ON FUNCTION pg_create_restore_point(text) TO task_backup;
