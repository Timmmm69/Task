-- Stage 2.3: organization-owned notification urgency scale (PostgreSQL 15+)
CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE TABLE IF NOT EXISTS notification_urgency_scales (
    organization_id uuid PRIMARY KEY REFERENCES organizations(id), version bigint NOT NULL DEFAULT 1 CHECK (version >= 1),
    updated_at timestamptz NOT NULL DEFAULT now(), updated_by_user_id uuid NULL REFERENCES users(id)
);
CREATE TABLE IF NOT EXISTS notification_urgency_scale_intervals (
    organization_id uuid NOT NULL REFERENCES notification_urgency_scales(organization_id) ON DELETE CASCADE,
    urgency_level text NOT NULL CHECK (urgency_level IN ('low','normal','high','critical')),
    min_score integer NOT NULL CHECK (min_score BETWEEN 0 AND 100), max_score integer NOT NULL CHECK (max_score BETWEEN 0 AND 100),
    display_token varchar(64) NOT NULL, PRIMARY KEY (organization_id, urgency_level), CHECK (min_score <= max_score),
    EXCLUDE USING gist (organization_id WITH =, int4range(min_score, max_score, '[]') WITH &&)
);
CREATE INDEX IF NOT EXISTS ix_notification_urgency_scale_intervals_order ON notification_urgency_scale_intervals (organization_id, min_score);
-- Application transaction validation requires exactly four levels, sorted contiguous coverage [0,100], and one row per level.
-- Seed defaults: low 0-24, normal 25-49, high 50-74, critical 75-100; semantic urgency is stored independently of display_token.
-- Versioned PUT/reset records audit action notification_urgency_scale.changed in the existing audit_entries history.
-- Search employee projection uses existing users/departments indexes; authorization and blocked-user policy run before cursor pagination.
