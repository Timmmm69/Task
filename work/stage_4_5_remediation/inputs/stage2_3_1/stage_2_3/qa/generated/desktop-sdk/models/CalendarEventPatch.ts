/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ContactAttendee } from './ContactAttendee';
import type { EventAttendee } from './EventAttendee';
/**
 * PATCH semantics: omitted properties remain unchanged; an explicit null clears only nullable properties; readOnly properties are rejected; at least one writable property is required.
 */
export type CalendarEventPatch = {
    projectId?: string | null;
    title?: string;
    description?: string | null;
    /**
     * Calendar date without a time zone.
     */
    eventDate?: string;
    isAllDay?: boolean;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    startAtUtc?: string | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    endAtUtc?: string | null;
    timeZone?: string;
    status?: 'scheduled' | 'cancelled';
    userAttendees?: Array<EventAttendee>;
    contactAttendees?: Array<ContactAttendee>;
};

