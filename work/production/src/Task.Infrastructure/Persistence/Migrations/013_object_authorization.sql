-- SEC-02: one query-time relationship policy for every product projection.
ALTER TABLE iam.user_roles ADD COLUMN department_id uuid REFERENCES core.objects(id);
ALTER TABLE iam.user_roles ADD COLUMN valid_until timestamptz;
CREATE FUNCTION iam.capability_code(capability text) RETURNS text LANGUAGE sql IMMUTABLE AS $$
 SELECT CASE lower(capability) WHEN 'calendar.read' THEN 'task.read' ELSE lower(capability) END;
$$;

CREATE FUNCTION iam.object_department(org uuid, object_id uuid) RETURNS uuid LANGUAGE sql STABLE AS $$
 SELECT ep.department_id FROM core.objects o
 LEFT JOIN work.tasks task ON task.organization_id=org AND task.id=o.id
 LEFT JOIN projects.projects project ON project.organization_id=org AND project.id=CASE WHEN o.object_type='project' THEN o.id ELSE task.project_id END
 JOIN iam.user_accounts u ON u.organization_id=org AND u.id=CASE WHEN o.object_type='user_account' THEN o.id ELSE COALESCE(project.owner_user_id,o.created_by) END
 JOIN org.employee_profiles ep ON ep.organization_id=org AND ep.id=u.employee_profile_id
 WHERE o.organization_id=org AND o.id=object_id;
$$;
CREATE FUNCTION iam.scope_denied(org uuid, actor uuid, object_id uuid, capability text) RETURNS boolean LANGUAGE sql STABLE AS $$
 SELECT EXISTS(SELECT 1 FROM iam.user_roles ur JOIN iam.roles r ON r.id=ur.role_id JOIN iam.role_permissions rp ON rp.role_id=r.id
 WHERE r.organization_id=org AND ur.user_account_id=actor AND rp.permission_code=iam.capability_code(capability) AND rp.effect='deny'
 AND (ur.valid_until IS NULL OR ur.valid_until>statement_timestamp())
 AND (ur.department_id IS NULL OR ur.department_id=iam.object_department(org,object_id)));
$$;
CREATE FUNCTION iam.scope_allows(org uuid, actor uuid, object_id uuid, capability text) RETURNS boolean LANGUAGE sql STABLE AS $$
 -- A scoped grant is a restriction of existing object relationships, never an organization-wide grant.
 SELECT NOT EXISTS(SELECT 1 FROM iam.user_roles ur JOIN iam.roles r ON r.id=ur.role_id JOIN iam.role_permissions rp ON rp.role_id=r.id
 WHERE r.organization_id=org AND ur.user_account_id=actor AND rp.permission_code=iam.capability_code(capability) AND rp.effect='grant'
 AND (ur.valid_until IS NULL OR ur.valid_until>statement_timestamp()) AND ur.department_id IS NOT NULL)
 OR EXISTS(SELECT 1 FROM iam.user_roles ur JOIN iam.roles r ON r.id=ur.role_id JOIN iam.role_permissions rp ON rp.role_id=r.id
 WHERE r.organization_id=org AND ur.user_account_id=actor AND rp.permission_code=iam.capability_code(capability) AND rp.effect='grant'
 AND (ur.valid_until IS NULL OR ur.valid_until>statement_timestamp()) AND (ur.department_id IS NULL OR ur.department_id=iam.object_department(org,object_id)));
$$;
-- Preserve manually edited cards; materialize security attributes of untouched legacy occurrences.
UPDATE work.tasks t SET card_content=jsonb_strip_nulls(jsonb_build_object(
 'projectId',r.template->'projectId','requesterUserId',r.template->'requesterUserId',
 'primaryCounterpartyObjectId',r.template->'primaryCounterpartyObjectId',
 'assigneeIds',r.template->'assigneeIds','watcherIds',r.template->'watcherIds','description',r.template->'description'))
FROM calendar.recurrence_occurrences r JOIN core.objects o ON o.organization_id=r.organization_id AND o.id=r.task_id
WHERE t.organization_id=r.organization_id AND t.id=r.task_id AND t.card_content='{}'::jsonb AND r.generated_task_version=o.version
AND (r.template->>'projectId' IS NULL OR EXISTS(SELECT 1 FROM projects.projects p WHERE p.organization_id=r.organization_id AND p.id::text=r.template->>'projectId'));
UPDATE iam.authorization_scope_versions SET version=version+1,updated_at=statement_timestamp();
CREATE FUNCTION iam.permission_denied(org uuid, actor uuid, capability text) RETURNS boolean
LANGUAGE sql STABLE AS $$
 SELECT EXISTS(SELECT 1 FROM iam.user_roles ur JOIN iam.roles r ON r.id=ur.role_id
 JOIN iam.role_permissions rp ON rp.role_id=r.id
 WHERE r.organization_id=org AND ur.user_account_id=actor AND rp.permission_code=iam.capability_code(capability) AND rp.effect='deny'
 AND ur.department_id IS NULL AND (ur.valid_until IS NULL OR ur.valid_until>statement_timestamp()));
$$;

CREATE FUNCTION iam.permission_granted(org uuid, actor uuid, capability text) RETURNS boolean
LANGUAGE sql STABLE AS $$
 SELECT EXISTS(SELECT 1 FROM iam.user_accounts WHERE organization_id=org AND id=actor AND account_status='active')
 AND NOT iam.permission_denied(org,actor,capability) AND EXISTS(
 SELECT 1 FROM iam.user_roles ur JOIN iam.roles r ON r.id=ur.role_id JOIN iam.role_permissions rp ON rp.role_id=r.id
 JOIN iam.permissions p ON p.code=rp.permission_code AND p.is_active
 WHERE r.organization_id=org AND ur.user_account_id=actor AND rp.permission_code=iam.capability_code(capability) AND rp.effect='grant'
 AND ur.department_id IS NULL AND (ur.valid_until IS NULL OR ur.valid_until>statement_timestamp()));
$$;

CREATE FUNCTION iam.permission_available(org uuid, actor uuid, capability text) RETURNS boolean LANGUAGE sql STABLE AS $$
 SELECT EXISTS(SELECT 1 FROM iam.user_accounts WHERE organization_id=org AND id=actor AND account_status='active')
 AND NOT iam.permission_denied(org,actor,capability) AND EXISTS(
 SELECT 1 FROM iam.user_roles ur JOIN iam.roles r ON r.id=ur.role_id JOIN iam.role_permissions rp ON rp.role_id=r.id
 JOIN iam.permissions p ON p.code=rp.permission_code AND p.is_active
 WHERE r.organization_id=org AND ur.user_account_id=actor AND rp.permission_code=iam.capability_code(capability) AND rp.effect='grant'
 AND (ur.valid_until IS NULL OR ur.valid_until>statement_timestamp()));
$$;

CREATE FUNCTION iam.project_denied(org uuid, project uuid, actor uuid, capability text) RETURNS boolean
LANGUAGE sql STABLE AS $$
 SELECT iam.scope_denied(org,actor,project,capability) OR EXISTS(
 SELECT 1 FROM projects.members m WHERE m.organization_id=org AND m.project_id=project
 AND m.user_account_id=actor AND m.status='active' AND
 (COALESCE(m.permission_overrides->'deny' ? iam.capability_code(capability),false) OR EXISTS(
 SELECT 1 FROM iam.role_permissions rp WHERE rp.role_id=m.project_role_id AND rp.permission_code=iam.capability_code(capability) AND rp.effect='deny')));
$$;

CREATE FUNCTION iam.project_allowed(org uuid, project uuid, actor uuid, capability text, administrator boolean DEFAULT false) RETURNS boolean
LANGUAGE sql STABLE AS $$
 SELECT iam.scope_allows(org,actor,project,capability) AND NOT iam.project_denied(org,project,actor,capability) AND EXISTS(
 SELECT 1 FROM projects.projects p JOIN core.objects o ON o.organization_id=p.organization_id AND o.id=p.id
 WHERE p.organization_id=org AND p.id=project AND
 (administrator OR iam.permission_granted(org,actor,'organization.manage') OR p.owner_user_id=actor OR p.manager_user_id=actor OR EXISTS(
 SELECT 1 FROM projects.members m WHERE m.organization_id=org AND m.project_id=project AND m.user_account_id=actor AND m.status='active'
 AND (iam.capability_code(capability) IN ('project.read','task.read','calendar.read','contact.read','filecatalog.read','history.read','objectlink.read','filereference.open')
 OR COALESCE(m.permission_overrides->'allow' ? iam.capability_code(capability),false) OR EXISTS(
 SELECT 1 FROM iam.role_permissions rp WHERE rp.role_id=m.project_role_id AND rp.permission_code=iam.capability_code(capability) AND rp.effect='grant')))));
$$;

CREATE OR REPLACE FUNCTION work.task_project_visible(org uuid, project uuid, actor uuid) RETURNS boolean LANGUAGE sql STABLE AS $$
 SELECT project IS NULL OR (iam.project_allowed(org,project,actor,'project.read') AND EXISTS(SELECT 1 FROM core.objects WHERE organization_id=org AND id=project AND lifecycle_state<>'trashed'));
$$;
CREATE OR REPLACE FUNCTION work.task_project_writable(org uuid, project uuid, actor uuid, permission text) RETURNS boolean LANGUAGE sql STABLE AS $$
 SELECT NOT iam.permission_denied(org,actor,permission) AND (project IS NULL OR
 (iam.project_allowed(org,project,actor,permission) AND EXISTS(SELECT 1 FROM core.objects WHERE organization_id=org AND id=project AND lifecycle_state='active')));
$$;
CREATE OR REPLACE FUNCTION work.task_visible(org uuid, task uuid, actor uuid) RETURNS boolean LANGUAGE sql STABLE AS $$
 SELECT iam.scope_allows(org,actor,task,'task.read') AND NOT iam.scope_denied(org,actor,task,'task.read') AND EXISTS(
 SELECT 1 FROM work.tasks t JOIN core.objects o ON o.organization_id=t.organization_id AND o.id=t.id
 WHERE t.organization_id=org AND t.id=task AND NOT iam.project_denied(org,t.project_id,actor,'task.read') AND
 (iam.permission_granted(org,actor,'organization.manage') OR (t.project_id IS NOT NULL AND work.task_project_visible(org,t.project_id,actor) AND iam.project_allowed(org,t.project_id,actor,'task.read'))
 OR o.created_by=actor OR t.card_content->>'requesterUserId'=actor::text OR t.card_content->'assigneeIds' ? actor::text OR t.card_content->'watcherIds' ? actor::text));
$$;

CREATE FUNCTION iam.object_allowed(org uuid, object_id uuid, actor uuid, capability text, administrator boolean DEFAULT false) RETURNS boolean
LANGUAGE sql STABLE AS $$
 -- Follow only security-bearing relations towards their parent. UNION terminates cycles.
 WITH RECURSIVE scopes(id) AS (
 SELECT object_id
 UNION
 SELECT edge.parent FROM scopes s JOIN LATERAL (
 SELECT l.source_object_id AS parent FROM core.object_links l WHERE l.organization_id=org AND l.target_object_id=s.id
 AND l.link_type IN ('project_file','project_contact','task_file','task_contact','contact_file')
 UNION SELECT p.parent_item_id FROM files.catalog_items p WHERE p.organization_id=org AND p.id=s.id AND p.parent_item_id IS NOT NULL
 UNION SELECT i.counterparty_object_id FROM crm.interactions i WHERE i.organization_id=org AND i.id=s.id
 ) edge ON true
 ), attributes AS (
 SELECT o.*,t.project_id AS task_project,t.card_content,e.project_id AS event_project
 FROM scopes s JOIN core.objects o ON o.id=s.id AND o.organization_id=org
 LEFT JOIN work.tasks t ON t.organization_id=org AND t.id=o.id
 LEFT JOIN calendar.events e ON e.organization_id=org AND e.id=o.id
 WHERE o.lifecycle_state<>'trashed' OR o.id=object_id
 )
 SELECT NOT iam.permission_denied(org,actor,capability)
 AND EXISTS(SELECT 1 FROM attributes a WHERE iam.scope_allows(org,actor,a.id,capability))
 AND NOT EXISTS(SELECT 1 FROM attributes a WHERE iam.scope_denied(org,actor,a.id,capability))
 AND NOT EXISTS(SELECT 1 FROM attributes a WHERE iam.project_denied(org,
 CASE WHEN a.object_type='project' THEN a.id ELSE COALESCE(a.task_project,a.event_project) END,actor,capability))
 AND EXISTS(SELECT 1 FROM attributes a WHERE
 administrator OR iam.permission_granted(org,actor,'organization.manage') OR
 (a.object_type='project' AND iam.project_allowed(org,a.id,actor,capability,administrator)) OR
 (a.object_type='task' AND ((iam.capability_code(capability) IN ('task.read','contact.read','filecatalog.read','history.read','objectlink.read','filereference.open','comment.create') AND work.task_visible(org,a.id,actor))
 OR (a.task_project IS NOT NULL AND iam.project_allowed(org,a.task_project,actor,capability,administrator))
 OR (a.task_project IS NULL AND a.created_by=actor)
 OR (iam.capability_code(capability) IN ('task.changestatus','objectlink.create') AND (a.created_by=actor OR a.card_content->'assigneeIds' ? actor::text)))) OR
 (a.object_type='calendar_event' AND (a.created_by=actor OR (a.event_project IS NOT NULL AND iam.project_allowed(org,a.event_project,actor,capability,administrator))
 OR (iam.capability_code(capability) IN ('calendar.read','history.read','objectlink.read') AND EXISTS(SELECT 1 FROM calendar.event_user_attendees ea WHERE ea.organization_id=org AND ea.event_id=a.id AND ea.user_account_id=actor)))) OR
 (a.object_type IN ('contact','company','catalog_item','interaction') AND a.created_by=actor) OR
 (a.object_type IN ('network_resource','employee_profile')));
$$;

CREATE INDEX ix_object_links_authorization ON core.object_links(organization_id,target_object_id,link_type,source_object_id);

INSERT INTO iam.permissions(code,description) VALUES ('user.manageroles','Assign organization roles'),('role.read','Read role templates') ON CONFLICT DO NOTHING;
INSERT INTO iam.role_permissions(role_id,permission_code,effect)
SELECT rp.role_id,p.code,rp.effect FROM iam.role_permissions rp CROSS JOIN (VALUES ('user.manageroles'),('role.read')) p(code)
WHERE rp.permission_code='identity.role.manage' ON CONFLICT DO NOTHING;

-- Explicit allowlists: future permissions are never silently given to business roles.
CREATE TABLE iam.system_role_templates(role_code text NOT NULL, permission_code varchar(128) NOT NULL REFERENCES iam.permissions(code), PRIMARY KEY(role_code,permission_code));
INSERT INTO iam.system_role_templates(role_code,permission_code)
SELECT role,code FROM (VALUES ('system_manager'),('system_employee'),('system_observer')) roles(role)
CROSS JOIN unnest(ARRAY['project.read','task.read','filecatalog.read','filereference.open','objectlink.read','search.use','settings.readown','settings.updateown','notification.readown','notification.manageown','session.readownorall','session.revokeownorall','device.readownorall','device.updateownorall','device.revoke']) p(code)
WHERE EXISTS(SELECT 1 FROM iam.permissions WHERE iam.permissions.code=p.code);
INSERT INTO iam.system_role_templates(role_code,permission_code)
SELECT role,code FROM (VALUES ('system_manager'),('system_employee')) roles(role)
CROSS JOIN unnest(ARRAY['task.create','task.changestatus','comment.create','contact.read','filecatalog.create','objectlink.create','employee.read','calendarevent.create','calendarevent.update','calendarevent.delete','recurrence.read','recurrence.manage']) p(code)
WHERE EXISTS(SELECT 1 FROM iam.permissions WHERE iam.permissions.code=p.code);
INSERT INTO iam.system_role_templates(role_code,permission_code)
SELECT 'system_manager',code FROM unnest(ARRAY['project.create','project.update','project.archive','project.managemembers','task.update','task.assign','task.watch','contact.create','contact.update','interaction.create','interaction.update','filecatalog.update','filelocation.update','history.read','objectlink.delete']) p(code)
WHERE EXISTS(SELECT 1 FROM iam.permissions WHERE iam.permissions.code=p.code);

CREATE FUNCTION iam.seed_system_roles(org uuid) RETURNS void LANGUAGE plpgsql AS $$
DECLARE template text; role_id uuid;
BEGIN
 FOREACH template IN ARRAY ARRAY['system_manager','system_employee','system_observer'] LOOP
  IF EXISTS(SELECT 1 FROM iam.roles WHERE organization_id=org AND code=template AND NOT is_system) THEN
   RAISE EXCEPTION 'Reserved system role code collision: %', template;
  END IF;
  INSERT INTO iam.roles(id,organization_id,code,display_name,is_system)
  VALUES(gen_random_uuid(),org,template,CASE template WHEN 'system_manager' THEN 'Руководитель' WHEN 'system_employee' THEN 'Сотрудник' ELSE 'Наблюдатель' END,true)
  ON CONFLICT(organization_id,code) DO NOTHING;
  SELECT id INTO role_id FROM iam.roles WHERE organization_id=org AND code=template;
  INSERT INTO iam.role_permissions(role_id,permission_code,effect)
  SELECT role_id,t.permission_code,'grant' FROM iam.system_role_templates t WHERE t.role_code=template ON CONFLICT DO NOTHING;
 END LOOP;
END $$;
DO $$ DECLARE org uuid; BEGIN FOR org IN SELECT id FROM core.organizations LOOP PERFORM iam.seed_system_roles(org); END LOOP; END $$;
CREATE FUNCTION iam.seed_new_organization_roles() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN PERFORM iam.seed_system_roles(NEW.id); RETURN NEW; END $$;
CREATE TRIGGER organization_system_roles AFTER INSERT ON core.organizations FOR EACH ROW EXECUTE FUNCTION iam.seed_new_organization_roles();
-- Provisioning is migrator-only; runtime cannot modify templates or create privileged roles.
REVOKE ALL ON FUNCTION iam.seed_system_roles(uuid), iam.seed_new_organization_roles() FROM PUBLIC;

CREATE FUNCTION iam.invalidate_role_scope() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE actor uuid;
BEGIN
 actor := CASE WHEN TG_OP='DELETE' THEN OLD.user_account_id ELSE NEW.user_account_id END;
 INSERT INTO iam.authorization_scope_versions(user_account_id,version) VALUES(actor,2)
 ON CONFLICT(user_account_id) DO UPDATE SET version=iam.authorization_scope_versions.version+1,updated_at=statement_timestamp();
 RETURN NULL;
END $$;
CREATE TRIGGER user_role_scope AFTER INSERT OR UPDATE OR DELETE ON iam.user_roles FOR EACH ROW EXECUTE FUNCTION iam.invalidate_role_scope();

CREATE FUNCTION iam.recurrence_allowed(org uuid, series uuid, actor uuid, capability text) RETURNS boolean LANGUAGE sql STABLE AS $$
 SELECT NOT iam.permission_denied(org,actor,capability) AND EXISTS(
 SELECT 1 FROM calendar.recurrence_series s WHERE s.organization_id=org AND s.id=series
 AND iam.scope_allows(org,actor,s.created_by,capability) AND NOT iam.scope_denied(org,actor,s.created_by,capability)
 AND NOT iam.project_denied(org,(s.definition->'template'->>'projectId')::uuid,actor,capability)
 AND (iam.permission_granted(org,actor,'organization.manage') OR
 (s.definition->'template'->>'projectId' IS NULL AND s.created_by=actor) OR
 iam.project_allowed(org,(s.definition->'template'->>'projectId')::uuid,actor,capability)));
$$;
