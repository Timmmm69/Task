/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type FileLocationDeviceState = {
    deviceId: string;
    userAccountId: string;
    status: 'unknown' | 'available' | 'not_found' | 'access_denied' | 'resource_unavailable' | 'invalid_path' | 'timeout';
    lastCheckedAt?: string | null;
    latencyMs?: number | null;
    version: number;
};

