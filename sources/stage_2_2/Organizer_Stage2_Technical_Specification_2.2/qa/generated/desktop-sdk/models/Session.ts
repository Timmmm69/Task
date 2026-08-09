/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type Session = {
    id: string;
    userAccountId: string;
    deviceId: string;
    status: 'active' | 'revoked' | 'expired';
    createdAt: string;
    lastSeenAt: string;
    idleExpiresAt: string;
    absoluteExpiresAt: string;
};

