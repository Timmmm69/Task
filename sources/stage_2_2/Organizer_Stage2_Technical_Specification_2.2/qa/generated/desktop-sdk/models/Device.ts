/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type Device = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    deviceKey: string;
    deviceName: string;
    platform: 'windows' | 'linux' | 'macos';
    appVersion: string;
    status: 'active' | 'revoked';
    readonly lastSeenAt?: string | null;
};

