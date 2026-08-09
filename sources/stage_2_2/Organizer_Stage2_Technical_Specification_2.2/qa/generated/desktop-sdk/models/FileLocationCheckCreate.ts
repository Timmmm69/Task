/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type FileLocationCheckCreate = {
    deviceId: string;
    status: 'available' | 'not_found' | 'access_denied' | 'resource_unavailable' | 'invalid_path' | 'timeout';
    latencyMs?: number | null;
    osErrorCode?: string | null;
    checkedAt: string;
    expectedLocationVersion: number;
};

