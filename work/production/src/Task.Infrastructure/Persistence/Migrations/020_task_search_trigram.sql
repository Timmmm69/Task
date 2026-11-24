CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- The existing task substring search matches the title.
CREATE INDEX ix_tasks_search_title_trigram ON work.tasks USING gin (lower(title) gin_trgm_ops);
