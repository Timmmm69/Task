-- Only change affected rules during writes. The worker polls the delivery index and
-- checks a bounded batch of automatic deadlines when their next check becomes due.
ALTER TABLE calendar.task_deadline_scan ADD COLUMN next_check_at timestamptz NOT NULL DEFAULT '-infinity';
DROP INDEX calendar.ix_task_deadline_scan;
CREATE INDEX ix_task_deadline_next_check ON calendar.task_deadline_scan(next_check_at, organization_id, task_id);
CREATE INDEX ix_reminders_target ON calendar.reminders(organization_id, target_object_id);
CREATE TABLE calendar.reminder_recovery_queue (
 organization_id uuid NOT NULL,
 target_object_id uuid NOT NULL,
 first_due_at timestamptz NOT NULL,
 PRIMARY KEY (organization_id, target_object_id),
 FOREIGN KEY (organization_id, target_object_id) REFERENCES core.objects(organization_id, id) ON DELETE CASCADE
);
CREATE INDEX ix_reminder_recovery_due ON calendar.reminder_recovery_queue(first_due_at, organization_id, target_object_id);
INSERT INTO calendar.reminder_recovery_queue(organization_id, target_object_id, first_due_at)
SELECT organization_id, target_object_id, min(next_trigger_at)
FROM calendar.reminders
WHERE managed_source IS NULL AND status IN ('scheduled', 'due', 'snoozed')
GROUP BY organization_id, target_object_id;

CREATE FUNCTION calendar.schedule_task_deadline_check() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE expires timestamptz; local_day text; zone_name text;
BEGIN
 SELECT t.deadline_at, t.card_content->>'deadlineDate', t.card_content->>'deadlineTimeZone'
 INTO expires, local_day, zone_name
 FROM work.tasks t
 WHERE t.organization_id = NEW.organization_id AND t.id = NEW.task_id;
 IF expires IS NULL AND local_day IS NOT NULL AND zone_name IS NOT NULL THEN
  IF EXISTS (SELECT 1 FROM pg_timezone_names WHERE name = zone_name) THEN
   expires := (local_day::date + 1)::timestamp AT TIME ZONE zone_name;
  END IF;
 END IF;

 NEW.next_check_at := CASE WHEN expires <= NEW.checked_at THEN 'infinity'::timestamptz
                        ELSE least(expires, NEW.checked_at + interval '15 minutes') END;
 RETURN NEW;
END $$;
CREATE TRIGGER schedule_task_deadline_check BEFORE INSERT OR UPDATE OF checked_at ON calendar.task_deadline_scan
FOR EACH ROW EXECUTE FUNCTION calendar.schedule_task_deadline_check();

CREATE FUNCTION calendar.reconcile_due_deadlines(batch_size integer) RETURNS void
LANGUAGE plpgsql AS $$
DECLARE target record;
BEGIN
 FOR target IN
  SELECT s.organization_id, s.task_id
  FROM calendar.task_deadline_scan s
  JOIN core.objects o ON o.organization_id = s.organization_id AND o.id = s.task_id
  WHERE s.next_check_at <= statement_timestamp()
  ORDER BY s.next_check_at, s.organization_id, s.task_id
  LIMIT batch_size FOR UPDATE OF o SKIP LOCKED
 LOOP
  -- All reminder mutations take the target lock before changing its rules.
  -- Recheck in a new statement in case a concurrent transaction already processed it.
  IF EXISTS (SELECT 1 FROM calendar.task_deadline_scan s
             WHERE s.organization_id = target.organization_id AND s.task_id = target.task_id
             AND s.next_check_at <= clock_timestamp()) THEN
   PERFORM calendar.reconcile_task_deadline(target.organization_id, target.task_id);
  END IF;
 END LOOP;
 -- Drain legacy manual rules left behind if a worker was unavailable before the upgrade.
 FOR target IN
  SELECT q.organization_id, q.target_object_id
  FROM calendar.reminder_recovery_queue q
  JOIN core.objects o ON o.organization_id = q.organization_id AND o.id = q.target_object_id
  ORDER BY q.first_due_at, q.organization_id, q.target_object_id
  LIMIT batch_size FOR UPDATE OF o SKIP LOCKED
 LOOP
  DELETE FROM calendar.reminder_recovery_queue
  WHERE organization_id = target.organization_id AND target_object_id = target.target_object_id;
  IF FOUND THEN
   PERFORM calendar.reconcile_manual_reminders(target.organization_id, target.target_object_id);
  END IF;
 END LOOP;
END $$;

CREATE OR REPLACE FUNCTION calendar.task_deadline_saved() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE actor uuid; choice integer;
BEGIN
 choice := (NEW.card_content->>'deadlineReminderOffsetMinutes')::integer;
 IF TG_OP = 'UPDATE' THEN
  IF NEW.deadline_at IS DISTINCT FROM OLD.deadline_at OR
     NEW.card_content->>'deadlineDate' IS DISTINCT FROM OLD.card_content->>'deadlineDate' OR
     NEW.card_content->>'deadlineTimeZone' IS DISTINCT FROM OLD.card_content->>'deadlineTimeZone' THEN
   UPDATE calendar.task_deadline_preferences SET suppressed_due = NULL
   WHERE organization_id = NEW.organization_id AND task_id = NEW.id;
  ELSIF choice IS NULL AND OLD.card_content->>'deadlineReminderOffsetMinutes' IS NULL
    AND NEW.status = OLD.status AND NEW.project_id IS NOT DISTINCT FROM OLD.project_id
    AND NEW.card_content->'assigneeIds' IS NOT DISTINCT FROM OLD.card_content->'assigneeIds'
    AND NEW.card_content->'watcherIds' IS NOT DISTINCT FROM OLD.card_content->'watcherIds'
    AND NEW.card_content->'requesterUserId' IS NOT DISTINCT FROM OLD.card_content->'requesterUserId' THEN
   RETURN NULL;
  END IF;
 END IF;

 IF choice IS NOT NULL THEN
  IF choice NOT BETWEEN -1 AND 525600 THEN RAISE EXCEPTION 'Invalid reminder interval' USING ERRCODE = '23514'; END IF;
  SELECT updated_by INTO actor FROM core.objects WHERE organization_id = NEW.organization_id AND id = NEW.id;
  INSERT INTO calendar.task_deadline_preferences(organization_id, task_id, recipient_user_id, offset_minutes, date_only_time)
  SELECT NEW.organization_id, NEW.id, actor, choice, coalesce(s.date_only_reminder_time, '09:00'::time)
  FROM (SELECT 1) seed LEFT JOIN org.user_settings s ON s.organization_id = NEW.organization_id AND s.user_account_id = actor
  ON CONFLICT (organization_id, task_id, recipient_user_id) DO UPDATE
  SET offset_minutes = EXCLUDED.offset_minutes, suppressed_due = NULL;
  -- This private command field never remains in the shared task projection.
  UPDATE work.tasks SET card_content = card_content - 'deadlineReminderOffsetMinutes'
  WHERE organization_id = NEW.organization_id AND id = NEW.id;
 ELSE
  PERFORM calendar.reconcile_task_deadline(NEW.organization_id, NEW.id);
 END IF;
 RETURN NULL;
END $$;

CREATE FUNCTION calendar.invalidate_recipient_deadlines(org uuid, actor uuid, project uuid DEFAULT NULL) RETURNS void
LANGUAGE sql AS $$
 UPDATE calendar.task_deadline_scan s SET next_check_at = '-infinity'
 FROM work.tasks t JOIN core.objects o ON o.organization_id = t.organization_id AND o.id = t.id
 WHERE s.organization_id = org AND s.task_id = t.id AND t.organization_id = org
   AND s.next_check_at <> '-infinity' AND (project IS NULL OR t.project_id = project)
   AND (coalesce(t.card_content->'assigneeIds', '[]'::jsonb) ? actor::text
        OR (o.created_by = actor AND jsonb_array_length(coalesce(t.card_content->'assigneeIds', '[]'::jsonb)) = 0));
$$;

CREATE FUNCTION calendar.reminder_scope_changed() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE org uuid;
BEGIN
 SELECT organization_id INTO org FROM iam.user_accounts WHERE id = NEW.user_account_id;
 IF FOUND THEN PERFORM calendar.invalidate_recipient_deadlines(org, NEW.user_account_id); END IF;
 RETURN NULL;
END $$;
CREATE TRIGGER reminder_scope_changed AFTER INSERT OR UPDATE OF version ON iam.authorization_scope_versions
FOR EACH ROW EXECUTE FUNCTION calendar.reminder_scope_changed();

CREATE FUNCTION calendar.reminder_role_rule_changed() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE role_before uuid; role_after uuid; recipient record;
BEGIN
 IF TG_OP <> 'INSERT' AND OLD.permission_code IN ('task.read', 'project.read', 'organization.manage') THEN
  role_before := OLD.role_id;
 END IF;
 IF TG_OP <> 'DELETE' AND NEW.permission_code IN ('task.read', 'project.read', 'organization.manage') THEN
  role_after := NEW.role_id;
 END IF;
 IF role_before IS NULL AND role_after IS NULL THEN RETURN NULL; END IF;
 FOR recipient IN
  SELECT DISTINCT r.organization_id, ur.user_account_id
  FROM iam.user_roles ur JOIN iam.roles r ON r.id = ur.role_id
  WHERE ur.role_id IN (role_before, role_after)
  ORDER BY r.organization_id, ur.user_account_id
 LOOP
  PERFORM calendar.invalidate_recipient_deadlines(recipient.organization_id, recipient.user_account_id);
 END LOOP;
 RETURN NULL;
END $$;
CREATE TRIGGER reminder_role_rule_changed AFTER INSERT OR UPDATE OR DELETE ON iam.role_permissions
FOR EACH ROW EXECUTE FUNCTION calendar.reminder_role_rule_changed();

CREATE FUNCTION calendar.reminder_account_changed() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 PERFORM calendar.invalidate_recipient_deadlines(NEW.organization_id, NEW.id);
 RETURN NULL;
END $$;
CREATE TRIGGER reminder_account_changed AFTER UPDATE OF account_status, employee_profile_id ON iam.user_accounts
FOR EACH ROW WHEN (OLD.account_status IS DISTINCT FROM NEW.account_status
                OR OLD.employee_profile_id IS DISTINCT FROM NEW.employee_profile_id)
EXECUTE FUNCTION calendar.reminder_account_changed();

CREATE FUNCTION calendar.reminder_project_member_changed() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP IN ('UPDATE', 'DELETE') THEN
  PERFORM calendar.invalidate_recipient_deadlines(OLD.organization_id, OLD.user_account_id, OLD.project_id);
 END IF;
 IF TG_OP = 'INSERT' OR (TG_OP = 'UPDATE' AND
    (NEW.organization_id, NEW.user_account_id, NEW.project_id) IS DISTINCT FROM
    (OLD.organization_id, OLD.user_account_id, OLD.project_id)) THEN
  PERFORM calendar.invalidate_recipient_deadlines(NEW.organization_id, NEW.user_account_id, NEW.project_id);
 END IF;
 RETURN NULL;
END $$;
CREATE TRIGGER reminder_project_member_changed AFTER INSERT OR UPDATE OR DELETE ON projects.members
FOR EACH ROW EXECUTE FUNCTION calendar.reminder_project_member_changed();

CREATE FUNCTION calendar.reconcile_manual_reminders(org uuid, target uuid) RETURNS void LANGUAGE sql AS $$
 WITH definitions AS (
  SELECT r.id, o.lifecycle_state,
     CASE o.object_type WHEN 'task' THEN task.status WHEN 'calendar_event' THEN event.status END AS target_status,
     CASE r.trigger_type
       WHEN 'absolute' THEN r.absolute_trigger_at
       WHEN 'at_start' THEN coalesce(task.start_at_utc, event.start_at_utc)
       WHEN 'at_deadline' THEN coalesce(task.deadline_at, event.end_at_utc)
       WHEN 'before_start' THEN coalesce(task.start_at_utc, event.start_at_utc) - make_interval(mins => r.offset_minutes)
       WHEN 'before_deadline' THEN coalesce(task.deadline_at, event.end_at_utc) - make_interval(mins => r.offset_minutes)
     END AS due
  FROM calendar.reminders r JOIN core.objects o ON o.organization_id = r.organization_id AND o.id = r.target_object_id
  LEFT JOIN work.tasks task ON task.organization_id = o.organization_id AND task.id = o.id
  LEFT JOIN calendar.events event ON event.organization_id = o.organization_id AND event.id = o.id
  WHERE r.organization_id = org AND r.target_object_id = target AND r.managed_source IS NULL
    AND r.status IN ('scheduled', 'due', 'snoozed')
 )
 UPDATE calendar.reminders r SET
    status = CASE WHEN d.lifecycle_state <> 'active' OR d.target_status IS NULL OR d.target_status IN ('completed', 'cancelled')
      OR (r.status <> 'snoozed' AND d.due IS NULL) THEN 'expired' ELSE 'scheduled' END,
    next_trigger_at = coalesce(d.due, r.next_trigger_at), version = r.version + 1, updated_at = clock_timestamp()
 FROM definitions d WHERE r.id = d.id AND
   (d.lifecycle_state <> 'active' OR d.target_status IS NULL OR d.target_status IN ('completed', 'cancelled')
     OR (r.status <> 'snoozed' AND (d.due IS NULL OR r.next_trigger_at <> d.due)));
$$;

CREATE FUNCTION calendar.manual_relative_reminder_inserted() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 PERFORM calendar.reconcile_manual_reminders(NEW.organization_id, NEW.target_object_id);
 RETURN NULL;
END $$;
CREATE TRIGGER manual_relative_reminder_inserted AFTER INSERT ON calendar.reminders
FOR EACH ROW WHEN (NEW.managed_source IS NULL AND NEW.trigger_type <> 'absolute')
EXECUTE FUNCTION calendar.manual_relative_reminder_inserted();

CREATE FUNCTION calendar.reminder_target_timing_changed() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 PERFORM calendar.reconcile_manual_reminders(NEW.organization_id, NEW.id);
 RETURN NULL;
END $$;
CREATE TRIGGER reminder_task_timing_changed AFTER UPDATE OF status, start_at_utc, deadline_at ON work.tasks
FOR EACH ROW WHEN (OLD.status IS DISTINCT FROM NEW.status OR OLD.start_at_utc IS DISTINCT FROM NEW.start_at_utc
                OR OLD.deadline_at IS DISTINCT FROM NEW.deadline_at)
EXECUTE FUNCTION calendar.reminder_target_timing_changed();
CREATE TRIGGER reminder_event_timing_changed AFTER UPDATE OF status, start_at_utc, end_at_utc ON calendar.events
FOR EACH ROW WHEN (OLD.status IS DISTINCT FROM NEW.status OR OLD.start_at_utc IS DISTINCT FROM NEW.start_at_utc
                OR OLD.end_at_utc IS DISTINCT FROM NEW.end_at_utc)
EXECUTE FUNCTION calendar.reminder_target_timing_changed();

CREATE OR REPLACE FUNCTION calendar.task_deadline_lifecycle() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF NEW.lifecycle_state IS DISTINCT FROM OLD.lifecycle_state THEN
  IF NEW.object_type = 'task' THEN
   PERFORM calendar.reconcile_task_deadline(NEW.organization_id, NEW.id);
  END IF;
  IF NEW.object_type IN ('task', 'calendar_event') THEN
   PERFORM calendar.reconcile_manual_reminders(NEW.organization_id, NEW.id);
  ELSIF NEW.object_type = 'user_account' THEN
   PERFORM calendar.invalidate_recipient_deadlines(NEW.organization_id, NEW.id);
  ELSIF NEW.object_type = 'project' THEN
   UPDATE calendar.task_deadline_scan s SET next_check_at = '-infinity'
   FROM work.tasks t WHERE s.organization_id = NEW.organization_id AND t.organization_id = s.organization_id
    AND t.id = s.task_id AND t.project_id = NEW.id AND s.next_check_at <> '-infinity';
  END IF;
 END IF;
 RETURN NULL;
END $$;

CREATE FUNCTION calendar.cancel_stale_reminder_occurrences() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 UPDATE calendar.reminder_occurrences SET status = 'cancelled', claimed_by_worker = NULL,
     lock_token = NULL, lease_expires_at = NULL, heartbeat_at = NULL
 WHERE reminder_id = NEW.id AND status IN ('created', 'failed', 'claimed')
   AND (NEW.status IN ('expired', 'cancelled') OR due_at <> NEW.next_trigger_at);
 RETURN NULL;
END $$;
CREATE TRIGGER cancel_stale_reminder_occurrences AFTER UPDATE OF status, next_trigger_at ON calendar.reminders
FOR EACH ROW WHEN (NEW.next_trigger_at IS DISTINCT FROM OLD.next_trigger_at
                OR (NEW.status IS DISTINCT FROM OLD.status AND NEW.status IN ('expired', 'cancelled')))
EXECUTE FUNCTION calendar.cancel_stale_reminder_occurrences();

REVOKE ALL ON FUNCTION calendar.schedule_task_deadline_check(), calendar.reconcile_due_deadlines(integer),
 calendar.invalidate_recipient_deadlines(uuid,uuid,uuid), calendar.reminder_scope_changed(), calendar.reminder_role_rule_changed(),
 calendar.reminder_account_changed(), calendar.reminder_project_member_changed(),
 calendar.reconcile_manual_reminders(uuid,uuid), calendar.reminder_target_timing_changed(),
 calendar.manual_relative_reminder_inserted(), calendar.cancel_stale_reminder_occurrences() FROM PUBLIC;
DO $$ DECLARE runtime_role record; BEGIN
 FOR runtime_role IN
  SELECT grantee FROM information_schema.role_table_grants
  WHERE table_schema = 'calendar' AND table_name = 'task_deadline_scan' AND grantee <> 'PUBLIC'
    AND privilege_type IN ('SELECT', 'UPDATE', 'DELETE')
  GROUP BY grantee HAVING count(DISTINCT privilege_type) = 3
 LOOP
  EXECUTE format('GRANT EXECUTE ON FUNCTION calendar.reconcile_due_deadlines(integer),
   calendar.invalidate_recipient_deadlines(uuid,uuid,uuid), calendar.reconcile_manual_reminders(uuid,uuid) TO %I', runtime_role.grantee);
  EXECUTE format('GRANT SELECT,DELETE ON calendar.reminder_recovery_queue TO %I', runtime_role.grantee);
 END LOOP;
END $$;
