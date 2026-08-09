/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type LoginAttempt = {
    id: string;
    login: string;
    userAccountId?: string | null;
    deviceId?: string | null;
    ipAddress?: string | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    occurredAt: string;
    succeeded: boolean;
    failureCode?: string | null;
    correlationId: string;
};

