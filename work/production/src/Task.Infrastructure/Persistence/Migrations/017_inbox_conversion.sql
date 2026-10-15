-- Restore the existing Stage 2.2 inbox contract. Task workflow states remain unchanged.
CREATE TABLE work.inbox_items (
 id uuid PRIMARY KEY REFERENCES core.objects(id), organization_id uuid NOT NULL,
 owner_user_id uuid NOT NULL, item_type varchar(20) NOT NULL
 CHECK(item_type IN ('task','note','file_link','web_link','idea','assignment')),
 title text CHECK(length(title)<=500), content text CHECK(length(content)<=20000),
 raw_url text CHECK(length(raw_url)<=2048), raw_path text CHECK(length(raw_path)<=4096),
 status varchar(20) NOT NULL DEFAULT 'unprocessed' CHECK(status IN ('unprocessed','converted','discarded')),
 converted_object_id uuid, processed_at timestamptz,
 -- Compatibility reference only; never serialized in the public InboxItem DTO.
 legacy_task_id uuid UNIQUE,
 FOREIGN KEY(organization_id,id) REFERENCES core.objects(organization_id,id),
 FOREIGN KEY(organization_id,owner_user_id) REFERENCES iam.user_accounts(organization_id,id),
 FOREIGN KEY(organization_id,converted_object_id) REFERENCES core.objects(organization_id,id),
 FOREIGN KEY(organization_id,legacy_task_id) REFERENCES work.tasks(organization_id,id),
 CHECK(coalesce(length(btrim(title)),0)>0 OR coalesce(length(btrim(content)),0)>0 OR raw_url IS NOT NULL OR raw_path IS NOT NULL)
);
CREATE INDEX ix_inbox_owner_status ON work.inbox_items(organization_id,owner_user_id,status,id);

INSERT INTO iam.permissions(code,description) VALUES
 ('inbox.readown','Read own inbox items.'),('inbox.manageown','Manage and convert own inbox items.') ON CONFLICT DO NOTHING;
INSERT INTO iam.role_permissions(role_id,permission_code,effect)
 SELECT role_id,'inbox.readown',effect FROM iam.role_permissions WHERE permission_code='task.read' ON CONFLICT DO NOTHING;
INSERT INTO iam.role_permissions(role_id,permission_code,effect)
 SELECT role_id,'inbox.manageown',effect FROM iam.role_permissions WHERE permission_code='task.create' ON CONFLICT DO NOTHING;
INSERT INTO iam.system_role_templates(role_code,permission_code)
 SELECT role_code,'inbox.readown' FROM iam.system_role_templates WHERE permission_code='task.read' ON CONFLICT DO NOTHING;
INSERT INTO iam.system_role_templates(role_code,permission_code)
 SELECT role_code,'inbox.manageown' FROM iam.system_role_templates WHERE permission_code='task.create' ON CONFLICT DO NOTHING;

ALTER TABLE governance.domain_events DROP CONSTRAINT ck_domain_event_aggregate;
ALTER TABLE governance.domain_events ADD CONSTRAINT ck_domain_event_aggregate CHECK(aggregate_type IN
 ('task','calendar_event','recurrence_series','project','contact','company','catalog_item','network_resource','notification','interaction',
 'user-settings','organization-settings','preferences','user_account','device','reminder','inbox_item'));

-- Preserve the old heuristic inbox without cloning or deleting its tasks. Existing task
-- visibility is rechecked on every read/conversion of these compatibility records.
DO $$ DECLARE entry record; inbox_id uuid; BEGIN
 FOR entry IN SELECT o.*,t.title FROM work.tasks t JOIN core.objects o ON o.organization_id=t.organization_id AND o.id=t.id
 WHERE o.lifecycle_state='active' AND t.status='new' AND t.start_at_utc IS NULL AND t.deadline_at IS NULL
 AND coalesce(nullif(t.card_content->'assigneeIds','null'::jsonb),'[]'::jsonb)='[]'::jsonb
 AND coalesce(nullif(t.card_content->'watcherIds','null'::jsonb),'[]'::jsonb)='[]'::jsonb
 AND NOT EXISTS(SELECT 1 FROM jsonb_each(t.card_content) f WHERE f.key IN
 ('description','projectId','parentTaskId','requesterUserId','primaryCounterpartyObjectId','scheduledDate','startTimeLocal',
 'scheduleTimeZone','plannedDurationMinutes','deadlineDate') AND f.value<>'null'::jsonb
 AND NOT(jsonb_typeof(f.value)='string' AND btrim(f.value#>>'{}')='')) LOOP
  inbox_id:=gen_random_uuid();
  INSERT INTO core.objects(id,organization_id,object_type,created_at,created_by,updated_at,updated_by)
  VALUES(inbox_id,entry.organization_id,'inbox_item',entry.created_at,entry.created_by,entry.updated_at,entry.updated_by);
  INSERT INTO work.inbox_items(id,organization_id,owner_user_id,item_type,title,legacy_task_id)
  VALUES(inbox_id,entry.organization_id,entry.created_by,'task',entry.title,entry.id);
 END LOOP;
END $$;
DO $$ DECLARE runtime_role record; BEGIN
 FOR runtime_role IN SELECT DISTINCT grantee FROM information_schema.role_table_grants
 WHERE table_schema='work' AND table_name='tasks' AND privilege_type='INSERT' AND grantee<>'PUBLIC' LOOP
  EXECUTE format('GRANT SELECT,INSERT,UPDATE ON work.inbox_items TO %I',runtime_role.grantee);
 END LOOP;
END $$;
