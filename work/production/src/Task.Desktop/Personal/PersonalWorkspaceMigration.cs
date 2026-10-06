namespace Task.Desktop.Personal;

internal static class PersonalWorkspaceMigration
{
    internal const string Sql = """
        CREATE TABLE personal_catalog (
            id TEXT PRIMARY KEY, version INTEGER NOT NULL CHECK(version>0), name TEXT NOT NULL,
            item_type TEXT NOT NULL CHECK(item_type IN ('file_reference','folder_reference','virtual_folder')),
            description TEXT, parent_id TEXT REFERENCES personal_catalog(id),
            lifecycle TEXT NOT NULL DEFAULT 'active' CHECK(lifecycle IN ('active','archived','trashed')),
            updated_at TEXT NOT NULL);
        CREATE INDEX personal_catalog_parent ON personal_catalog(parent_id,lifecycle,name);
        CREATE TABLE personal_file_locations (
            id TEXT PRIMARY KEY, catalog_id TEXT NOT NULL UNIQUE REFERENCES personal_catalog(id) ON DELETE CASCADE,
            raw_path TEXT NOT NULL);
        CREATE TABLE personal_contacts (
            id TEXT PRIMARY KEY, version INTEGER NOT NULL CHECK(version>0), first_name TEXT NOT NULL,
            last_name TEXT, display_name TEXT NOT NULL, notes TEXT,
            lifecycle TEXT NOT NULL DEFAULT 'active' CHECK(lifecycle IN ('active','archived','trashed')),
            updated_at TEXT NOT NULL);
        CREATE INDEX personal_contacts_active ON personal_contacts(lifecycle,display_name);
        CREATE TABLE object_links (
            id TEXT PRIMARY KEY, source_id TEXT NOT NULL, target_id TEXT NOT NULL,
            link_type TEXT NOT NULL CHECK(link_type IN ('task_file','project_file','contact_file','task_contact')),
            UNIQUE(source_id,target_id,link_type), CHECK(source_id<>target_id));
        CREATE INDEX object_links_target ON object_links(target_id);
        CREATE TABLE personal_lifecycle (
            object_id TEXT PRIMARY KEY, previous_state TEXT CHECK(previous_state IN ('active','archived')),
            archived_at TEXT, deleted_at TEXT, updated_at TEXT NOT NULL);
        CREATE TABLE personal_workspace_settings (key TEXT PRIMARY KEY, version INTEGER NOT NULL, payload TEXT NOT NULL);
        CREATE VIEW personal_objects AS
            SELECT id,'task' object_type,title,description search_text,version,
                CASE WHEN trashed_at IS NOT NULL THEN 'trashed' WHEN archived_at IS NOT NULL THEN 'archived' ELSE 'active' END lifecycle,
                updated_at FROM tasks
            UNION ALL SELECT id,'project',name,description,version,lifecycle,
                COALESCE((SELECT updated_at FROM personal_lifecycle WHERE object_id=personal_projects.id),'1970-01-01T00:00:00+00:00') FROM personal_projects
            UNION ALL SELECT id,'contact',display_name,COALESCE(first_name,'')||' '||COALESCE(last_name,'')||' '||COALESCE(notes,''),version,lifecycle,updated_at FROM personal_contacts
            UNION ALL SELECT id,'catalog_item',name,COALESCE(description,'')||' '||COALESCE((SELECT raw_path FROM personal_file_locations WHERE catalog_id=personal_catalog.id),''),version,lifecycle,updated_at FROM personal_catalog;
        CREATE INDEX personal_tasks_active ON tasks(trashed_at,archived_at,title);
        CREATE INDEX personal_projects_active ON personal_projects(lifecycle,name);
        PRAGMA user_version=4;
        """;
}
