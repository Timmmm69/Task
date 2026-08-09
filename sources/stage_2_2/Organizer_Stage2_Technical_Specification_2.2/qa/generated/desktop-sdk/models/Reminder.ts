/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type Reminder = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    targetObjectId: string;
    recipientUserId: string;
    triggerType: 'absolute' | 'before_start' | 'before_deadline' | 'at_start' | 'at_deadline';
    offsetMinutes?: number | null;
    absoluteTriggerAt?: string | null;
    nextTriggerAt: string;
    status: 'scheduled' | 'due' | 'delivered' | 'snoozed' | 'cancelled' | 'expired';
    snoozedUntil?: string | null;
    deliveredAt?: string | null;
};

