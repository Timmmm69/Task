-- Stage 2.3: organization-owned notification urgency scale (PostgreSQL 16).
BEGIN;

CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE TABLE IF NOT EXISTS notify.notification_urgency_scales (
    organization_id uuid PRIMARY KEY REFERENCES core.organizations(id) ON DELETE CASCADE,
    version bigint NOT NULL DEFAULT 1 CHECK (version >= 1),
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by_user_id uuid NULL REFERENCES iam.user_accounts(id) ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS notify.notification_urgency_scale_intervals (
    organization_id uuid NOT NULL REFERENCES notify.notification_urgency_scales(organization_id) ON DELETE CASCADE,
    urgency_level text NOT NULL CHECK (urgency_level IN ('low','normal','high','critical')),
    min_score integer NOT NULL CHECK (min_score BETWEEN 0 AND 100),
    max_score integer NOT NULL CHECK (max_score BETWEEN 0 AND 100),
    display_token varchar(64) NOT NULL CHECK (length(btrim(display_token)) > 0),
    PRIMARY KEY (organization_id, urgency_level),
    CHECK (min_score <= max_score),
    EXCLUDE USING gist (organization_id WITH =, int4range(min_score, max_score, '[]') WITH &&)
);

CREATE INDEX IF NOT EXISTS ix_notification_urgency_scale_intervals_order
    ON notify.notification_urgency_scale_intervals (organization_id, min_score);

CREATE OR REPLACE FUNCTION notify.enforce_notification_urgency_scale_complete()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    target_organization_id uuid := COALESCE(NEW.organization_id, OLD.organization_id);
    interval_count integer;
    first_score integer;
    last_score integer;
    gap_count integer;
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM notify.notification_urgency_scales
        WHERE organization_id = target_organization_id
    ) THEN
        RETURN NULL;
    END IF;

    SELECT count(*), min(min_score), max(max_score)
    INTO interval_count, first_score, last_score
    FROM notify.notification_urgency_scale_intervals
    WHERE organization_id = target_organization_id;

    SELECT count(*)
    INTO gap_count
    FROM (
        SELECT min_score,
               lag(max_score) OVER (ORDER BY min_score, urgency_level) AS previous_max
        FROM notify.notification_urgency_scale_intervals
        WHERE organization_id = target_organization_id
    ) ordered_intervals
    WHERE previous_max IS NOT NULL
      AND min_score <> previous_max + 1;

    IF interval_count <> 4 OR first_score <> 0 OR last_score <> 100 OR gap_count <> 0 THEN
        RAISE EXCEPTION
            'Notification urgency scale for organization % must contain four contiguous intervals covering 0..100',
            target_organization_id
            USING ERRCODE = '23514';
    END IF;

    RETURN NULL;
END;
$$;

DROP TRIGGER IF EXISTS trg_notification_urgency_scale_complete
    ON notify.notification_urgency_scale_intervals;
CREATE CONSTRAINT TRIGGER trg_notification_urgency_scale_complete
AFTER INSERT OR UPDATE OR DELETE ON notify.notification_urgency_scale_intervals
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION notify.enforce_notification_urgency_scale_complete();

INSERT INTO notify.notification_urgency_scales (organization_id)
SELECT id
FROM core.organizations
ON CONFLICT (organization_id) DO NOTHING;

INSERT INTO notify.notification_urgency_scale_intervals
    (organization_id, urgency_level, min_score, max_score, display_token)
SELECT organizations.id, defaults.urgency_level, defaults.min_score, defaults.max_score, defaults.display_token
FROM core.organizations AS organizations
CROSS JOIN (
    VALUES
        ('low', 0, 24, 'urgency.low'),
        ('normal', 25, 49, 'urgency.normal'),
        ('high', 50, 74, 'urgency.high'),
        ('critical', 75, 100, 'urgency.critical')
) AS defaults(urgency_level, min_score, max_score, display_token)
ON CONFLICT (organization_id, urgency_level) DO NOTHING;

-- Versioned PUT/reset records audit action notification_urgency_scale.changed in the existing audit_entries history.
-- Search employee projection uses existing users/departments indexes; authorization and blocked-user policy run before cursor pagination.

COMMIT;
