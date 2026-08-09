\set ON_ERROR_STOP on

INSERT INTO core.organizations (id, code, name, default_time_zone)
VALUES (
    '00000000-0000-7000-8000-000000000001',
    'runtime-validation',
    'Runtime Validation',
    'Europe/Minsk'
);

INSERT INTO core.organization_settings (organization_id)
VALUES ('00000000-0000-7000-8000-000000000001');

INSERT INTO core.objects (id, organization_id, object_type)
VALUES
    ('00000000-0000-7000-8000-000000000010', '00000000-0000-7000-8000-000000000001', 'department'),
    ('00000000-0000-7000-8000-000000000011', '00000000-0000-7000-8000-000000000001', 'employee_profile'),
    ('00000000-0000-7000-8000-000000000012', '00000000-0000-7000-8000-000000000001', 'user_account');

INSERT INTO org.departments (id, organization_id, code, name)
VALUES (
    '00000000-0000-7000-8000-000000000010',
    '00000000-0000-7000-8000-000000000001',
    'validation',
    'Validation'
);

INSERT INTO org.employee_profiles (
    id,
    organization_id,
    department_id,
    first_name,
    last_name,
    display_name,
    job_title,
    work_email
)
VALUES (
    '00000000-0000-7000-8000-000000000011',
    '00000000-0000-7000-8000-000000000001',
    '00000000-0000-7000-8000-000000000010',
    'Runtime',
    'Validator',
    'Runtime Validator',
    'QA Engineer',
    'runtime.validator@example.invalid'
);

INSERT INTO iam.user_accounts (
    id,
    organization_id,
    employee_profile_id,
    login,
    password_hash,
    account_status
)
VALUES (
    '00000000-0000-7000-8000-000000000012',
    '00000000-0000-7000-8000-000000000001',
    '00000000-0000-7000-8000-000000000011',
    'runtime.validator',
    'runtime-validation-only',
    'active'
);
