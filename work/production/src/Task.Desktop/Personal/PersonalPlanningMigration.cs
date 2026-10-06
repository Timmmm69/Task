namespace Task.Desktop.Personal;

/// <summary>Additive v2 → v3 migration, executed in the database's migration transaction.</summary>
internal static class PersonalPlanningMigration
{
    internal const string Sql = """
        CREATE TABLE personal_projects (
            id TEXT PRIMARY KEY, version INTEGER NOT NULL CHECK(version > 0),
            name TEXT NOT NULL, description TEXT, status INTEGER NOT NULL CHECK(status BETWEEN 0 AND 3),
            start_date TEXT, planned_end_date TEXT, actual_end_at TEXT,
            time_zone TEXT, color TEXT, lifecycle TEXT NOT NULL CHECK(lifecycle IN ('active','archived','trashed')));
        CREATE TABLE personal_events (
            id TEXT PRIMARY KEY, version INTEGER NOT NULL CHECK(version > 0),
            payload TEXT NOT NULL, project_id TEXT REFERENCES personal_projects(id));
        CREATE TABLE personal_series (
            id TEXT PRIMARY KEY, version INTEGER NOT NULL CHECK(version > 0), definition TEXT NOT NULL,
            horizon TEXT, project_id TEXT REFERENCES personal_projects(id));
        CREATE TABLE personal_occurrences (
            series_id TEXT NOT NULL REFERENCES personal_series(id), local_date TEXT NOT NULL,
            task_id TEXT NOT NULL UNIQUE REFERENCES tasks(id), generated_version INTEGER NOT NULL,
            skipped INTEGER NOT NULL DEFAULT 0 CHECK(skipped IN (0,1)),
            is_exception INTEGER NOT NULL DEFAULT 0 CHECK(is_exception IN (0,1)),
            template TEXT NOT NULL, dst_adjustment TEXT NOT NULL,
            PRIMARY KEY(series_id, local_date));
        CREATE TABLE personal_reminders (
            id TEXT PRIMARY KEY, version INTEGER NOT NULL CHECK(version > 0),
            target_id TEXT NOT NULL, target_kind INTEGER NOT NULL CHECK(target_kind IN (0,1)),
            trigger_type INTEGER NOT NULL, offset_minutes INTEGER, absolute_at TEXT,
            due_at TEXT, state TEXT NOT NULL CHECK(state IN ('pending','due','delivered','cancelled','expired')),
            dedupe_key TEXT UNIQUE, delivered_at TEXT, last_delivery TEXT);
        CREATE INDEX personal_reminders_pending ON personal_reminders(state, due_at);
        CREATE TABLE personal_notifications (
            id TEXT PRIMARY KEY, reminder_id TEXT NOT NULL REFERENCES personal_reminders(id),
            dedupe_key TEXT NOT NULL UNIQUE, target_id TEXT NOT NULL, title TEXT NOT NULL,
            due_at TEXT NOT NULL, delivered_at TEXT NOT NULL, is_read INTEGER NOT NULL DEFAULT 0,
            presentation_state TEXT NOT NULL DEFAULT 'pending');
        CREATE TABLE personal_recurrence_commands (
            key TEXT PRIMARY KEY, fingerprint TEXT NOT NULL, result TEXT NOT NULL);
        CREATE TRIGGER personal_task_project_insert BEFORE INSERT ON tasks
        WHEN NEW.project_id IS NOT NULL AND NOT EXISTS
            (SELECT 1 FROM personal_projects WHERE id=NEW.project_id AND lifecycle='active')
        BEGIN SELECT RAISE(ABORT,'Invalid Personal project'); END;
        CREATE TRIGGER personal_task_project_update BEFORE UPDATE OF project_id ON tasks
        WHEN NEW.project_id IS NOT NULL AND NEW.project_id IS NOT OLD.project_id AND NOT EXISTS
            (SELECT 1 FROM personal_projects WHERE id=NEW.project_id AND lifecycle='active')
        BEGIN SELECT RAISE(ABORT,'Invalid Personal project'); END;
        PRAGMA user_version=3;
        """;
}
