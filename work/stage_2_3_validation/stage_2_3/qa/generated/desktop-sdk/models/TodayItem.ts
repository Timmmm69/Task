/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type TodayItem = {
    objectId: string;
    itemType: 'task' | 'calendar_event' | 'reminder';
    title: string;
    /**
     * Calendar date without a time zone.
     */
    localDate: string;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    startAtUtc?: string | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    endAtUtc?: string | null;
    isAllDay: boolean;
    projectId?: string | null;
    status: string;
    priority?: 'low' | 'normal' | 'high' | 'critical';
    recipientUserId?: string | null;
};

