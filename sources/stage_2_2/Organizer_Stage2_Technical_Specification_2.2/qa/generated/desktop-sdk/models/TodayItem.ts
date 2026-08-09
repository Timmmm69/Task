/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type TodayItem = {
    objectId: string;
    itemType: 'task' | 'calendar_event' | 'reminder';
    title: string;
    localDate: string;
    startAtUtc?: string | null;
    endAtUtc?: string | null;
    isAllDay: boolean;
    projectId?: string | null;
    status: string;
    priority?: 'low' | 'normal' | 'high' | 'critical';
    recipientUserId?: string | null;
};

