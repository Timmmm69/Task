CREATE SCHEMA IF NOT EXISTS calendar;

CREATE TABLE calendar.events (
    id uuid PRIMARY KEY,
    organization_id uuid NOT NULL,
    project_id uuid,
    title text NOT NULL,
    description text,
    event_date date NOT NULL,
    is_all_day boolean NOT NULL DEFAULT false,
    start_at_utc timestamptz,
    end_at_utc timestamptz,
    time_zone_id varchar(64) NOT NULL,
    status varchar(16) NOT NULL DEFAULT 'scheduled',
    CONSTRAINT uq_events_org_id UNIQUE (organization_id, id),
    CONSTRAINT fk_events_object_org FOREIGN KEY (organization_id, id)
        REFERENCES core.objects(organization_id, id) ON DELETE RESTRICT,
    CONSTRAINT ck_events_title CHECK (length(btrim(title)) BETWEEN 1 AND 500),
    CONSTRAINT ck_events_description CHECK (description IS NULL OR length(description) <= 20000),
    CONSTRAINT ck_events_status CHECK (status IN ('scheduled', 'cancelled')),
    CONSTRAINT ck_events_time_zone CHECK (length(btrim(time_zone_id)) BETWEEN 1 AND 64),
    CONSTRAINT ck_events_timing CHECK (
        (is_all_day = false AND start_at_utc IS NOT NULL AND end_at_utc IS NOT NULL AND
            end_at_utc > start_at_utc)
        OR
        (is_all_day = true AND start_at_utc IS NULL AND end_at_utc IS NULL)
    )
);

CREATE INDEX ix_events_org_timing
    ON calendar.events (organization_id, event_date, is_all_day, start_at_utc);
CREATE INDEX ix_events_org_status
    ON calendar.events (organization_id, status);

CREATE TABLE calendar.event_user_attendees (
    event_id uuid NOT NULL,
    organization_id uuid NOT NULL,
    position smallint NOT NULL,
    user_account_id uuid NOT NULL,
    role varchar(16) NOT NULL,
    response_status varchar(16) NOT NULL,
    responded_at timestamptz,
    CONSTRAINT pk_event_user_attendees PRIMARY KEY (event_id, position),
    CONSTRAINT fk_event_user_attendees_event FOREIGN KEY (organization_id, event_id)
        REFERENCES calendar.events(organization_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_event_user_attendees_position CHECK (position >= 0),
    CONSTRAINT ck_event_user_attendees_role CHECK (role IN ('required', 'optional', 'observer')),
    CONSTRAINT ck_event_user_attendees_response CHECK (
        response_status IN ('pending', 'accepted', 'declined', 'tentative')
    )
);

CREATE INDEX ix_event_user_attendees_org_user
    ON calendar.event_user_attendees (organization_id, user_account_id, event_id);

CREATE TABLE calendar.event_contact_attendees (
    event_id uuid NOT NULL,
    organization_id uuid NOT NULL,
    position smallint NOT NULL,
    contact_id uuid NOT NULL,
    role varchar(16) NOT NULL,
    response_status varchar(16) NOT NULL,
    responded_at timestamptz,
    CONSTRAINT pk_event_contact_attendees PRIMARY KEY (event_id, position),
    CONSTRAINT fk_event_contact_attendees_event FOREIGN KEY (organization_id, event_id)
        REFERENCES calendar.events(organization_id, id) ON DELETE CASCADE,
    CONSTRAINT ck_event_contact_attendees_position CHECK (position >= 0),
    CONSTRAINT ck_event_contact_attendees_role CHECK (role IN ('required', 'optional', 'observer')),
    CONSTRAINT ck_event_contact_attendees_response CHECK (
        response_status IN ('pending', 'accepted', 'declined', 'tentative')
    )
);

CREATE INDEX ix_event_contact_attendees_org_contact
    ON calendar.event_contact_attendees (organization_id, contact_id, event_id);
