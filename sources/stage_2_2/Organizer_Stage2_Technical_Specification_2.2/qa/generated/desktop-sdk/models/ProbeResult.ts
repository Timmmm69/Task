/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ProbeResult = {
    resourceId: string;
    deviceId: string;
    status: 'available' | 'not_found' | 'access_denied' | 'timeout' | 'invalid_path';
    latencyMs?: number | null;
    checkedAt: string;
};

