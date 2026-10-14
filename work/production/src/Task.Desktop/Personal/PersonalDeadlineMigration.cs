namespace Task.Desktop.Personal;

internal static class PersonalDeadlineMigration
{
    internal const string Sql = """
        ALTER TABLE tasks ADD COLUMN deadline_date TEXT;
        ALTER TABLE tasks ADD COLUMN deadline_zone TEXT;
        ALTER TABLE tasks ADD COLUMN deadline_reminder_offset INTEGER CHECK(deadline_reminder_offset BETWEEN -1 AND 525600);
        ALTER TABLE tasks ADD COLUMN deadline_reminder_time TEXT;
        ALTER TABLE tasks ADD COLUMN deadline_suppressed_due TEXT;
        ALTER TABLE personal_reminders ADD COLUMN managed_source TEXT;
        CREATE UNIQUE INDEX personal_task_deadline_reminder ON personal_reminders(target_id) WHERE managed_source='task_deadline';
        CREATE TABLE personal_task_commands (key TEXT PRIMARY KEY, fingerprint TEXT NOT NULL, result TEXT NOT NULL);
        PRAGMA user_version=5;
        """;
}
