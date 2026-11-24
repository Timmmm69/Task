CREATE TABLE work.material_inventory (
    organization_id uuid PRIMARY KEY REFERENCES core.organizations(id),
    revision bigint NOT NULL CHECK (revision > 0),
    document jsonb NOT NULL CHECK (jsonb_typeof(document) = 'object'),
    manager_id uuid,
    FOREIGN KEY (organization_id, manager_id) REFERENCES iam.user_accounts(organization_id, id)
);
