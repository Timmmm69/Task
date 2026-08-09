/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ExportJob = {
    jobId: string;
    status: 'queued' | 'running' | 'succeeded' | 'failed' | 'expired';
    downloadUrl?: string | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    expiresAt?: string | null;
};

