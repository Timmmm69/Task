/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type BackgroundJobRun = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    readonly createdAt?: string;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    readonly updatedAt?: string;
    jobCode: string;
    status: 'queued' | 'running' | 'succeeded' | 'failed' | 'dead_letter' | 'cancelled';
    attempt: number;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    scheduledAt: string;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    startedAt?: string | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    finishedAt?: string | null;
    errorCode?: string | null;
};

