-- Role permission rules gain an explicit effect: 'grant' admits the permission,
-- 'deny' explicitly refuses it and outranks any grant for the same code.
-- Existing rows (seeded by 002) are backfilled as grants.

ALTER TABLE iam.role_permissions
    ADD COLUMN effect varchar(8) NOT NULL DEFAULT 'grant';

ALTER TABLE iam.role_permissions
    DROP CONSTRAINT role_permissions_pkey;

ALTER TABLE iam.role_permissions
    ADD CONSTRAINT role_permissions_pkey PRIMARY KEY (role_id, permission_code, effect);

ALTER TABLE iam.role_permissions
    ADD CONSTRAINT ck_role_permissions_effect CHECK (effect IN ('grant', 'deny'));