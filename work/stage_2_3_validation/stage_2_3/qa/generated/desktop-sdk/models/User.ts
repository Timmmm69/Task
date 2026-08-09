/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type User = {
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
    displayName: string;
    firstName: string;
    lastName: string;
    login: string;
    workEmail?: string | null;
    departmentId?: string | null;
    jobTitle?: string | null;
    accountStatus: 'pending_activation' | 'active' | 'blocked' | 'deactivated';
};

