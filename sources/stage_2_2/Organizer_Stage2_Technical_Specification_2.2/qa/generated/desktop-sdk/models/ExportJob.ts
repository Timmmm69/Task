/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ExportJob = {
    jobId: string;
    status: 'queued' | 'running' | 'succeeded' | 'failed' | 'expired';
    downloadUrl?: string | null;
    expiresAt?: string | null;
};

