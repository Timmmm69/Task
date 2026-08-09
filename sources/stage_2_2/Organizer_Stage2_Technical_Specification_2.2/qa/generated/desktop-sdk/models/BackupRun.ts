/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type BackupRun = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    backupType: 'base' | 'incremental' | 'wal_archive' | 'config' | 'restore_test';
    status: 'running' | 'succeeded' | 'failed' | 'cancelled';
    startedAt: string;
    finishedAt?: string | null;
    encrypted: boolean;
    sizeBytes?: number | null;
    checksum?: string | null;
};

