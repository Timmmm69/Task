/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ContactAttendee } from './ContactAttendee';
import type { EventAttendee } from './EventAttendee';
export type CalendarEvent = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    projectId?: string | null;
    title: string;
    description?: string | null;
    eventDate: string;
    isAllDay: boolean;
    startAtUtc?: string | null;
    endAtUtc?: string | null;
    timeZone: string;
    status: 'scheduled' | 'cancelled';
    userAttendees: Array<EventAttendee>;
    contactAttendees: Array<ContactAttendee>;
};

