-- API-04: relational data missing from the initial product projections.
CREATE TABLE crm.companies (
    id uuid PRIMARY KEY REFERENCES core.objects(id), organization_id uuid NOT NULL,
    name text NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 500),
    legal_name text CHECK (length(legal_name) <= 500), industry text CHECK (length(industry) <= 200),
    website text CHECK (length(website) <= 2048), tax_identifier text CHECK (length(tax_identifier) <= 100),
    notes text CHECK (length(notes) <= 20000), status text NOT NULL DEFAULT 'active' CHECK (status IN ('active','inactive')),
    UNIQUE (organization_id,id), FOREIGN KEY (organization_id,id) REFERENCES core.objects(organization_id,id)
);
CREATE TABLE projects.members (
    organization_id uuid NOT NULL, project_id uuid NOT NULL, user_account_id uuid NOT NULL,
    project_role_id uuid NOT NULL REFERENCES iam.roles(id), status text NOT NULL DEFAULT 'active'
        CHECK (status IN ('invited','active','removed')),
    joined_at timestamptz, removed_at timestamptz, version integer NOT NULL DEFAULT 1 CHECK (version > 0),
    permission_overrides jsonb NOT NULL DEFAULT '{"allow":[],"deny":[]}',
    PRIMARY KEY (organization_id,project_id,user_account_id),
    FOREIGN KEY (organization_id,project_id) REFERENCES projects.projects(organization_id,id),
    FOREIGN KEY (organization_id,user_account_id) REFERENCES iam.user_accounts(organization_id,id)
);
CREATE TABLE crm.communication_channels (
    id uuid PRIMARY KEY, organization_id uuid NOT NULL, owner_object_id uuid NOT NULL,
    channel_type text NOT NULL CHECK (channel_type IN ('phone','email','telegram','whatsapp','viber','other_messenger','website')),
    label text CHECK (length(label) <= 100), value text NOT NULL CHECK (length(btrim(value)) BETWEEN 1 AND 1000),
    is_primary boolean NOT NULL DEFAULT false, is_verified boolean NOT NULL DEFAULT false,
    FOREIGN KEY (organization_id,owner_object_id) REFERENCES crm.contacts(organization_id,id)
);
CREATE TABLE crm.addresses (
    id uuid PRIMARY KEY, organization_id uuid NOT NULL, owner_object_id uuid NOT NULL,
    address_type text NOT NULL CHECK (address_type IN ('work','legal','postal','other')),
    country_code text CHECK (length(country_code)=2), region text CHECK (length(region)<=200),
    city text CHECK (length(city)<=200), street text CHECK (length(street)<=500), postal_code text CHECK (length(postal_code)<=40),
    formatted_address text NOT NULL CHECK (length(btrim(formatted_address)) BETWEEN 1 AND 1000),
    is_primary boolean NOT NULL DEFAULT false,
    FOREIGN KEY (organization_id,owner_object_id) REFERENCES crm.contacts(organization_id,id)
);
CREATE TABLE crm.company_contacts (
    organization_id uuid NOT NULL, company_id uuid NOT NULL, contact_id uuid NOT NULL,
    job_title text CHECK (length(job_title)<=200), department_name text CHECK (length(department_name)<=200),
    is_primary boolean NOT NULL DEFAULT false, valid_from date, valid_to date,
    PRIMARY KEY (organization_id,company_id,contact_id), CHECK (valid_to IS NULL OR valid_from IS NULL OR valid_to>=valid_from),
    FOREIGN KEY (organization_id,company_id) REFERENCES crm.companies(organization_id,id),
    FOREIGN KEY (organization_id,contact_id) REFERENCES crm.contacts(organization_id,id)
);
CREATE TABLE files.network_resources (
    id uuid PRIMARY KEY REFERENCES core.objects(id), organization_id uuid NOT NULL,
    name text NOT NULL CHECK (length(btrim(name)) BETWEEN 1 AND 300),
    root_unc_path text NOT NULL CHECK (length(root_unc_path) BETWEEN 3 AND 4096),
    status text NOT NULL DEFAULT 'active' CHECK (status IN ('active','disabled','unavailable')),
    description text CHECK (length(description)<=20000),
    UNIQUE (organization_id,id), FOREIGN KEY (organization_id,id) REFERENCES core.objects(organization_id,id)
);
CREATE TABLE files.file_locations (
    id uuid PRIMARY KEY, organization_id uuid NOT NULL, catalog_item_id uuid NOT NULL,
    location_type text NOT NULL CHECK (location_type IN ('local_path','unc_path','mapped_drive')),
    raw_path text NOT NULL CHECK (length(btrim(raw_path)) BETWEEN 1 AND 4096),
    device_id uuid, owner_user_id uuid NOT NULL,
    network_resource_id uuid, priority integer NOT NULL DEFAULT 0 CHECK (priority BETWEEN 0 AND 32767),
    is_enabled boolean NOT NULL DEFAULT true, is_primary boolean NOT NULL DEFAULT false,
    version integer NOT NULL DEFAULT 1 CHECK (version>0),
    FOREIGN KEY (organization_id,catalog_item_id) REFERENCES files.catalog_items(organization_id,id),
    FOREIGN KEY (organization_id,network_resource_id) REFERENCES files.network_resources(organization_id,id),
    FOREIGN KEY (organization_id,owner_user_id) REFERENCES iam.user_accounts(organization_id,id),
    FOREIGN KEY (organization_id,device_id) REFERENCES iam.devices(organization_id,id),
    CHECK (location_type='unc_path' OR device_id IS NOT NULL)
);
CREATE INDEX ix_locations_catalog ON files.file_locations(organization_id,catalog_item_id,priority);
CREATE INDEX ix_members_user ON projects.members(organization_id,user_account_id,project_id) WHERE status='active';
CREATE INDEX ix_channels_contact ON crm.communication_channels(organization_id,owner_object_id);
CREATE INDEX ix_addresses_contact ON crm.addresses(organization_id,owner_object_id);
CREATE TABLE crm.interactions (
    id uuid PRIMARY KEY REFERENCES core.objects(id), organization_id uuid NOT NULL,
    counterparty_object_id uuid NOT NULL, interaction_type text NOT NULL CHECK (interaction_type IN ('call','meeting','email','agreement','note','next_step')),
    occurred_at timestamptz NOT NULL, subject text NOT NULL CHECK (length(btrim(subject)) BETWEEN 1 AND 500),
    details text CHECK (length(details)<=20000), next_step text CHECK (length(next_step)<=5000), next_step_due_at timestamptz,
    participant_object_ids uuid[] NOT NULL DEFAULT '{}',
    FOREIGN KEY (organization_id,id) REFERENCES core.objects(organization_id,id),
    FOREIGN KEY (organization_id,counterparty_object_id) REFERENCES core.objects(organization_id,id),
    CHECK (cardinality(participant_object_ids)<=500)
);
CREATE TABLE core.object_links (
    id uuid PRIMARY KEY, organization_id uuid NOT NULL, source_object_id uuid NOT NULL, target_object_id uuid NOT NULL,
    link_type text NOT NULL CHECK (link_type IN ('related','task_file','project_file','contact_file','task_contact','project_contact','task_project','parent_reference')),
    created_by uuid NOT NULL, created_at timestamptz NOT NULL DEFAULT statement_timestamp(),
    UNIQUE(organization_id,source_object_id,target_object_id,link_type), CHECK(source_object_id<>target_object_id),
    FOREIGN KEY (organization_id,source_object_id) REFERENCES core.objects(organization_id,id),
    FOREIGN KEY (organization_id,target_object_id) REFERENCES core.objects(organization_id,id)
);
CREATE INDEX ix_object_links_target ON core.object_links(organization_id,target_object_id,source_object_id);
CREATE TABLE files.location_checks (
    organization_id uuid NOT NULL, location_id uuid NOT NULL REFERENCES files.file_locations(id) ON DELETE CASCADE,
    device_id uuid NOT NULL, location_version integer NOT NULL, status text NOT NULL
        CHECK (status IN ('available','not_found','access_denied','resource_unavailable','invalid_path','timeout')),
    checked_at timestamptz NOT NULL, latency_ms integer CHECK(latency_ms>=0), os_error_code text CHECK(length(os_error_code)<=80),
    PRIMARY KEY(organization_id,location_id,device_id),
    FOREIGN KEY (organization_id,device_id) REFERENCES iam.devices(organization_id,id)
);

CREATE TABLE iam.product_api_commands (
    organization_id uuid NOT NULL, user_account_id uuid NOT NULL, operation text NOT NULL,
    idempotency_key varchar(200) NOT NULL, request_hash char(64) NOT NULL,
    response jsonb NOT NULL, created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (organization_id,user_account_id,operation,idempotency_key),
    FOREIGN KEY (organization_id,user_account_id) REFERENCES iam.user_accounts(organization_id,id)
);
CREATE TABLE core.product_search_snapshots (
    id uuid PRIMARY KEY, organization_id uuid NOT NULL, user_account_id uuid NOT NULL,
    filter_hash text NOT NULL, scope_version bigint NOT NULL, results jsonb NOT NULL,
    expires_at timestamptz NOT NULL,
    FOREIGN KEY (organization_id,user_account_id) REFERENCES iam.user_accounts(organization_id,id)
);
ALTER TABLE governance.domain_events DROP CONSTRAINT ck_domain_event_aggregate;
ALTER TABLE governance.domain_events ADD CONSTRAINT ck_domain_event_aggregate CHECK (aggregate_type IN
 ('task','calendar_event','project','contact','company','catalog_item','network_resource','notification','interaction',
 'user-settings','organization-settings','preferences'));

INSERT INTO iam.permissions(code,description)
SELECT lower(code),code FROM (VALUES
 ('Project.Read'),('Project.Create'),('Project.Update'),('Project.Delete'),('Project.Archive'),('Project.Restore'),
 ('Project.ManageMembers'),('Project.TransferOwnership'),('Contact.Read'),('Contact.Create'),('Contact.Update'),
 ('Contact.Delete'),('Contact.Restore'),('FileCatalog.Read'),('FileCatalog.Create'),('FileCatalog.Update'),
 ('FileCatalog.Delete'),('FileCatalog.Restore'),('FileReference.Open'),('FileLocation.Update'),
 ('FileLocation.ReadSensitivePath'),('NetworkResource.Manage'),('Notification.ReadOwn'),('Notification.ManageOwn'),
 ('Settings.ReadOwn'),('Settings.UpdateOwn'),('Organization.Read'),('Organization.Update'),('Search.Use'),
 ('History.Read'),('Archive.Restore'),('Trash.Read'),('Trash.Restore'),('Employee.Read'),
 ('Interaction.Create'),('Interaction.Update'),('ObjectLink.Read'),('ObjectLink.Create'),('ObjectLink.Delete')
) p(code) ON CONFLICT DO NOTHING;
-- Existing organization administrators receive new capabilities, including explicit denies.
INSERT INTO iam.role_permissions(role_id,permission_code,effect)
SELECT rp.role_id,p.code,rp.effect FROM iam.role_permissions rp CROSS JOIN iam.permissions p
WHERE rp.permission_code='organization.manage' AND p.code IN
 ('project.read','project.create','project.update','project.delete','project.archive','project.restore',
 'project.managemembers','project.transferownership','contact.read','contact.create','contact.update','contact.delete',
 'contact.restore','filecatalog.read','filecatalog.create','filecatalog.update','filecatalog.delete','filecatalog.restore',
 'filereference.open','filelocation.update','filelocation.readsensitivepath','networkresource.manage',
 'notification.readown','notification.manageown','settings.readown','settings.updateown',
 'organization.read','organization.update','search.use','history.read','archive.restore','trash.read','trash.restore','employee.read',
 'interaction.create','interaction.update','objectlink.read','objectlink.create','objectlink.delete')
ON CONFLICT DO NOTHING;
