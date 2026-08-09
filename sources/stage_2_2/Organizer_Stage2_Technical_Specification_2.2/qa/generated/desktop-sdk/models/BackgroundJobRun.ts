/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type BackgroundJobRun = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    jobCode: string;
    status: 'queued' | 'running' | 'succeeded' | 'failed' | 'dead_letter' | 'cancelled';
    attempt: number;
    scheduledAt: string;
    startedAt?: string | null;
    finishedAt?: string | null;
    errorCode?: string | null;
};

