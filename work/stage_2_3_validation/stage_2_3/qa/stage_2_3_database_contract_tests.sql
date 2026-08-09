\set ON_ERROR_STOP on

DO $$
DECLARE
    scale_count integer;
    interval_count integer;
    default_count integer;
    preserved_name text;
BEGIN
    SELECT count(*)
    INTO scale_count
    FROM notify.notification_urgency_scales
    WHERE organization_id = '00000000-0000-7000-8000-000000000001';

    SELECT count(*)
    INTO interval_count
    FROM notify.notification_urgency_scale_intervals
    WHERE organization_id = '00000000-0000-7000-8000-000000000001';

    SELECT count(*)
    INTO default_count
    FROM notify.notification_urgency_scale_intervals
    WHERE organization_id = '00000000-0000-7000-8000-000000000001'
      AND (
          (urgency_level = 'low' AND min_score = 0 AND max_score = 24 AND display_token = 'urgency.low')
          OR (urgency_level = 'normal' AND min_score = 25 AND max_score = 49 AND display_token = 'urgency.normal')
          OR (urgency_level = 'high' AND min_score = 50 AND max_score = 74 AND display_token = 'urgency.high')
          OR (urgency_level = 'critical' AND min_score = 75 AND max_score = 100 AND display_token = 'urgency.critical')
      );

    SELECT display_name
    INTO preserved_name
    FROM org.employee_profiles
    WHERE id = '00000000-0000-7000-8000-000000000011';

    IF scale_count <> 1 THEN
        RAISE EXCEPTION 'Expected one organization urgency scale, got %', scale_count;
    END IF;
    IF interval_count <> 4 OR default_count <> 4 THEN
        RAISE EXCEPTION 'Expected four default urgency intervals, got interval_count=%, default_count=%',
            interval_count, default_count;
    END IF;
    IF preserved_name <> 'Runtime Validator' THEN
        RAISE EXCEPTION 'Stage 2.2 employee data was not preserved';
    END IF;
    IF (SELECT count(*) FROM iam.permissions) <> 91 THEN
        RAISE EXCEPTION 'Permission catalog seed count changed';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes
        WHERE schemaname = 'notify'
          AND indexname = 'ix_notification_urgency_scale_intervals_order'
    ) THEN
        RAISE EXCEPTION 'Urgency interval ordering index is missing';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes
        WHERE schemaname = 'org'
          AND indexname = 'ix_employee_profiles_display_trgm'
    ) THEN
        RAISE EXCEPTION 'Employee search trigram index is missing';
    END IF;
END;
$$;

SELECT 'STAGE_2_3_DATABASE_CONTRACT_TESTS_PASS' AS result;
