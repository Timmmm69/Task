-- Preserve previously scheduled at-deadline deliveries; historical/backfill suppression is unchanged.
-- Preserve the delivered behavior when upgrading Windows package databases to the main catalog.
CREATE OR REPLACE FUNCTION calendar.reconcile_task_deadline(org uuid, target uuid, backfill boolean DEFAULT false) RETURNS void
LANGUAGE plpgsql AS $$
DECLARE task work.tasks%ROWTYPE; obj core.objects%ROWTYPE; recipient uuid; recipients uuid[];
 pref calendar.task_deadline_preferences%ROWTYPE; rule calendar.reminders%ROWTYPE;
 anchor timestamptz; due timestamptz; active boolean; wanted text; choice integer;
BEGIN
 SELECT * INTO obj FROM core.objects WHERE organization_id=org AND id=target FOR UPDATE;
 SELECT * INTO task FROM work.tasks WHERE organization_id=org AND id=target;
 IF NOT FOUND THEN RETURN; END IF;
 INSERT INTO calendar.task_deadline_scan(organization_id,task_id,checked_at) VALUES(org,target,clock_timestamp())
 ON CONFLICT(organization_id,task_id) DO UPDATE SET checked_at=EXCLUDED.checked_at;
 SELECT array_agg(value::uuid) INTO recipients FROM jsonb_array_elements_text(coalesce(task.card_content->'assigneeIds','[]'::jsonb));
 recipients:=coalesce(recipients,ARRAY[obj.created_by]);
 active:=obj.lifecycle_state='active' AND task.status NOT IN ('completed','cancelled');
 FOR recipient IN SELECT unnest(recipients) LOOP
  IF NOT EXISTS(SELECT 1 FROM iam.user_accounts u JOIN core.objects o ON o.organization_id=u.organization_id AND o.id=u.id
      WHERE u.organization_id=org AND u.id=recipient AND u.account_status='active' AND o.lifecycle_state='active')
      OR NOT iam.object_allowed(org,target,recipient,'task.read',false) THEN CONTINUE; END IF;
  IF task.deadline_at IS NULL AND task.card_content->>'deadlineDate' IS NULL THEN CONTINUE; END IF;
  IF task.deadline_at IS NULL AND NOT EXISTS(SELECT 1 FROM pg_timezone_names WHERE name=task.card_content->>'deadlineTimeZone') THEN CONTINUE; END IF;
  INSERT INTO calendar.task_deadline_preferences(organization_id,task_id,recipient_user_id,offset_minutes,date_only_time)
   SELECT org,target,recipient,
    CASE WHEN backfill THEN coalesce((SELECT CASE WHEN r.status IN ('cancelled','expired') THEN -1 ELSE coalesce(r.offset_minutes,0) END
     FROM calendar.reminders r WHERE r.organization_id=org AND r.target_object_id=target AND r.recipient_user_id=recipient
     AND r.managed_source IS NULL AND r.trigger_type IN ('before_deadline','at_deadline') ORDER BY r.created_at LIMIT 1),s.default_reminder_offset_minutes,15)
     ELSE coalesce(s.default_reminder_offset_minutes,15) END,coalesce(s.date_only_reminder_time,'09:00'::time)
   FROM (SELECT 1) seed LEFT JOIN org.user_settings s ON s.organization_id=org AND s.user_account_id=recipient
   ON CONFLICT DO NOTHING;
  SELECT * INTO pref FROM calendar.task_deadline_preferences WHERE organization_id=org AND task_id=target AND recipient_user_id=recipient;
  anchor:=coalesce(task.deadline_at,calendar.deadline_local_anchor((task.card_content->>'deadlineDate')::date,
       pref.date_only_time,task.card_content->>'deadlineTimeZone'));
  due:=anchor-make_interval(mins=>greatest(pref.offset_minutes,0));
  IF backfill AND due<=clock_timestamp() THEN
   UPDATE calendar.task_deadline_preferences SET suppressed_due=due WHERE organization_id=org AND task_id=target AND recipient_user_id=recipient;
   pref.suppressed_due:=due;
  END IF;
  SELECT * INTO rule FROM calendar.reminders WHERE organization_id=org AND target_object_id=target
    AND recipient_user_id=recipient AND managed_source='task_deadline' FOR UPDATE;
  wanted:=CASE WHEN NOT active OR pref.offset_minutes<0 THEN 'cancelled'
    WHEN (task.deadline_at IS NOT NULL AND anchor<=clock_timestamp()
          AND NOT (pref.offset_minutes=0 AND coalesce(rule.absolute_trigger_at=due
                   AND rule.status IN ('scheduled','due','snoozed','delivered'),false))) OR
         (task.deadline_at IS NULL AND (task.card_content->>'deadlineDate')::date<(clock_timestamp() AT TIME ZONE (task.card_content->>'deadlineTimeZone'))::date)
         OR pref.suppressed_due=due THEN 'expired'
    WHEN EXISTS(SELECT 1 FROM calendar.reminder_occurrences x WHERE x.reminder_id=rule.id AND x.due_at=due AND x.status='delivered') THEN 'delivered'
    ELSE 'scheduled' END;
  IF rule.id IS NULL THEN
   IF wanted='cancelled' THEN CONTINUE; END IF;
   INSERT INTO calendar.reminders(id,organization_id,target_object_id,recipient_user_id,trigger_type,absolute_trigger_at,
    next_trigger_at,created_by,managed_source,status) VALUES(gen_random_uuid(),org,target,recipient,'absolute',due,due,obj.created_by,'task_deadline',wanted);
  ELSIF rule.absolute_trigger_at IS DISTINCT FROM due OR
      (rule.status NOT IN ('snoozed','due','delivered') AND rule.status<>wanted) OR wanted IN ('cancelled','expired') THEN
   UPDATE calendar.reminders SET absolute_trigger_at=due,next_trigger_at=due,status=wanted,version=version+1,updated_at=clock_timestamp(),snooze_count=0
    WHERE id=rule.id AND (absolute_trigger_at IS DISTINCT FROM due OR status<>wanted);
  END IF;
 END LOOP;
 UPDATE calendar.reminders r SET status='cancelled',version=version+1,updated_at=clock_timestamp()
 WHERE r.organization_id=org AND r.target_object_id=target AND r.managed_source='task_deadline' AND r.status NOT IN ('cancelled','expired')
 AND (NOT active OR (task.deadline_at IS NULL AND task.card_content->>'deadlineDate' IS NULL)
   OR NOT (r.recipient_user_id=ANY(recipients)) OR NOT iam.object_allowed(org,target,r.recipient_user_id,'task.read',false)
   OR NOT EXISTS(SELECT 1 FROM iam.user_accounts u JOIN core.objects o ON o.organization_id=u.organization_id AND o.id=u.id
     WHERE u.organization_id=org AND u.id=r.recipient_user_id AND u.account_status='active' AND o.lifecycle_state='active'));
 UPDATE calendar.reminder_occurrences x SET status='cancelled',claimed_by_worker=NULL,lock_token=NULL,lease_expires_at=NULL,heartbeat_at=NULL
 FROM calendar.reminders r WHERE r.organization_id=org AND r.target_object_id=target AND r.id=x.reminder_id
   AND x.status IN ('created','claimed','failed') AND (r.status IN ('cancelled','expired') OR x.due_at<>r.next_trigger_at);
 -- Keep delivered rows immutable. A previously cancelled, undelivered time can be rearmed.
 UPDATE calendar.reminder_occurrences x SET status='created',next_attempt_at=x.due_at,attempt_count=0
 FROM calendar.reminders r WHERE r.organization_id=org AND r.target_object_id=target AND r.id=x.reminder_id AND r.managed_source='task_deadline'
   AND r.status='scheduled' AND x.due_at=r.next_trigger_at AND x.status='cancelled'
   AND NOT EXISTS(SELECT 1 FROM notify.notifications n WHERE n.id=x.id);
 -- History is retained; superseded deliveries cease to be unread popup candidates.
 UPDATE core.objects nobj SET version=nobj.version+1,updated_at=clock_timestamp()
 FROM notify.notifications n JOIN calendar.reminders r ON r.id=(n.action_payload->>'reminderId')::uuid
 WHERE nobj.id=n.id AND nobj.organization_id=org AND n.organization_id=org AND n.source_object_id=target
 AND n.notification_type='reminder' AND n.status IN ('pending','delivered')
 AND (NOT active OR (r.managed_source='task_deadline' AND (r.status IN ('cancelled','expired') OR r.next_trigger_at<>n.not_before)));
 UPDATE notify.notifications n SET status='dismissed',dismissed_at=clock_timestamp()
 FROM calendar.reminders r WHERE r.id=(n.action_payload->>'reminderId')::uuid
 AND n.organization_id=org AND n.source_object_id=target AND n.notification_type='reminder' AND n.status IN ('pending','delivered')
 AND (NOT active OR (r.managed_source='task_deadline' AND (r.status IN ('cancelled','expired') OR r.next_trigger_at<>n.not_before)));
 IF NOT active OR (task.deadline_at IS NULL AND task.card_content->>'deadlineDate' IS NULL)
    OR task.deadline_at<=clock_timestamp() THEN
  DELETE FROM calendar.task_deadline_scan WHERE organization_id=org AND task_id=target;
 END IF;
END $$;
