/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ReminderOccurrence = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    reminderId: string;
    dueAt: string;
    status: 'created' | 'claimed' | 'delivered' | 'failed' | 'dead_letter' | 'cancelled';
    attemptCount: number;
    nextAttemptAt: string;
};

