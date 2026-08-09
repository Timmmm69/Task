/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type RecurrenceTemplateReminderRule = {
    id: string;
    recipientUserId?: string | null;
    triggerType: 'before_start' | 'before_deadline' | 'at_start' | 'at_deadline';
    offsetMinutes?: number | null;
};

