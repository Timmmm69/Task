-- Additive task-card persistence; core.objects remains the concurrency/lifecycle owner.
ALTER TABLE work.tasks ADD COLUMN card_content jsonb NOT NULL DEFAULT '{}';
ALTER TABLE work.tasks ADD CONSTRAINT ck_tasks_card_object CHECK (jsonb_typeof(card_content) = 'object');
ALTER TABLE work.tasks ADD COLUMN project_id uuid GENERATED ALWAYS AS ((card_content->>'projectId')::uuid) STORED;
ALTER TABLE work.tasks ADD COLUMN parent_task_id uuid GENERATED ALWAYS AS ((card_content->>'parentTaskId')::uuid) STORED;
ALTER TABLE work.tasks ADD CONSTRAINT fk_task_project FOREIGN KEY (organization_id,project_id) REFERENCES projects.projects(organization_id,id);
ALTER TABLE work.tasks ADD CONSTRAINT fk_task_parent FOREIGN KEY (organization_id,parent_task_id) REFERENCES work.tasks(organization_id,id);
CREATE INDEX ix_tasks_parent ON work.tasks(organization_id,parent_task_id) WHERE parent_task_id IS NOT NULL;

CREATE FUNCTION work.task_project_visible(org uuid, project uuid, actor uuid) RETURNS boolean
LANGUAGE sql STABLE AS $$
 SELECT project IS NULL OR EXISTS (
  SELECT 1 FROM projects.projects p JOIN core.objects o ON o.organization_id=p.organization_id AND o.id=p.id
  WHERE p.organization_id=org AND p.id=project AND o.lifecycle_state<>'trashed' AND
   (p.owner_user_id=actor OR p.manager_user_id=actor OR EXISTS (
    SELECT 1 FROM projects.members m WHERE m.organization_id=org AND m.project_id=project AND m.user_account_id=actor AND m.status='active')
    OR (SELECT COALESCE(bool_or(rp.effect='grant'),false) AND NOT COALESCE(bool_or(rp.effect='deny'),false)
      FROM iam.user_roles ur JOIN iam.roles r ON r.id=ur.role_id JOIN iam.role_permissions rp ON rp.role_id=r.id
      WHERE ur.user_account_id=actor AND r.organization_id=org AND rp.permission_code='organization.manage')));
$$;

CREATE FUNCTION work.task_visible(org uuid, task uuid, actor uuid) RETURNS boolean LANGUAGE sql STABLE AS $$
 SELECT EXISTS(SELECT 1 FROM work.tasks t JOIN core.objects o ON o.organization_id=t.organization_id AND o.id=t.id
 WHERE t.organization_id=org AND t.id=task AND (work.task_project_visible(org,t.project_id,actor) OR o.created_by=actor
 OR t.card_content->>'requesterUserId'=actor::text OR t.card_content->'assigneeIds' ? actor::text OR t.card_content->'watcherIds' ? actor::text));
$$;
CREATE FUNCTION work.task_project_writable(org uuid, project uuid, actor uuid, permission text) RETURNS boolean LANGUAGE sql STABLE AS $$
 SELECT project IS NULL OR EXISTS(SELECT 1 FROM projects.projects p JOIN core.objects o ON o.organization_id=p.organization_id AND o.id=p.id
 WHERE p.organization_id=org AND p.id=project AND o.lifecycle_state='active' AND (p.owner_user_id=actor OR p.manager_user_id=actor OR
 (SELECT COALESCE(bool_or(rp.effect='grant'),false) AND NOT COALESCE(bool_or(rp.effect='deny'),false) FROM iam.user_roles ur
 JOIN iam.roles r ON r.id=ur.role_id JOIN iam.role_permissions rp ON rp.role_id=r.id WHERE ur.user_account_id=actor AND r.organization_id=org AND rp.permission_code='organization.manage')
 OR EXISTS(SELECT 1 FROM projects.members m WHERE m.organization_id=org AND m.project_id=project AND m.user_account_id=actor AND m.status='active'
 AND NOT(m.permission_overrides->'deny' ? permission)
 AND NOT EXISTS(SELECT 1 FROM iam.role_permissions rp WHERE rp.role_id=m.project_role_id AND rp.permission_code=permission AND rp.effect='deny')
 AND ((m.permission_overrides->'allow' ? permission) OR EXISTS(SELECT 1 FROM iam.role_permissions rp WHERE rp.role_id=m.project_role_id AND rp.permission_code=permission AND rp.effect='grant')))));
$$;

INSERT INTO iam.permissions(code,description) VALUES ('task.assign','Assign task participants'),('task.watch','Manage task watchers') ON CONFLICT DO NOTHING;
INSERT INTO iam.role_permissions(role_id,permission_code,effect)
SELECT rp.role_id,p.code,rp.effect FROM iam.role_permissions rp CROSS JOIN (VALUES ('task.assign'),('task.watch')) p(code)
WHERE rp.permission_code='task.update' ON CONFLICT DO NOTHING;

CREATE TABLE work.task_checklist (
 id uuid PRIMARY KEY, organization_id uuid NOT NULL, task_id uuid NOT NULL,
 text text NOT NULL CHECK (length(btrim(text)) BETWEEN 1 AND 2000),
 is_completed boolean NOT NULL DEFAULT false, sort_order integer NOT NULL DEFAULT 0,
 updated_at timestamptz NOT NULL DEFAULT statement_timestamp(), updated_by uuid NOT NULL,
 FOREIGN KEY (organization_id,task_id) REFERENCES work.tasks(organization_id,id),
 FOREIGN KEY (organization_id,updated_by) REFERENCES iam.user_accounts(organization_id,id)
);
CREATE TABLE work.task_comments (
 id uuid PRIMARY KEY, organization_id uuid NOT NULL, task_id uuid NOT NULL,
 body text NOT NULL CHECK (length(btrim(body)) BETWEEN 1 AND 50000),
 created_at timestamptz NOT NULL DEFAULT statement_timestamp(), author_user_id uuid NOT NULL,
 FOREIGN KEY (organization_id,task_id) REFERENCES work.tasks(organization_id,id),
 FOREIGN KEY (organization_id,author_user_id) REFERENCES iam.user_accounts(organization_id,id)
);
CREATE TABLE work.task_dependencies (
 id uuid PRIMARY KEY, organization_id uuid NOT NULL, task_id uuid NOT NULL, predecessor_id uuid NOT NULL,
 CHECK (task_id<>predecessor_id), UNIQUE(organization_id,task_id,predecessor_id),
 FOREIGN KEY (organization_id,task_id) REFERENCES work.tasks(organization_id,id),
 FOREIGN KEY (organization_id,predecessor_id) REFERENCES work.tasks(organization_id,id)
);
CREATE INDEX ix_task_checklist ON work.task_checklist(organization_id,task_id,sort_order,id);
CREATE INDEX ix_task_comments ON work.task_comments(organization_id,task_id,created_at,id);

INSERT INTO iam.permissions(code,description) VALUES ('comment.create','Create comments on readable objects') ON CONFLICT DO NOTHING;
INSERT INTO iam.role_permissions(role_id,permission_code,effect) SELECT role_id,'comment.create',effect FROM iam.role_permissions WHERE permission_code='task.update' ON CONFLICT DO NOTHING;
