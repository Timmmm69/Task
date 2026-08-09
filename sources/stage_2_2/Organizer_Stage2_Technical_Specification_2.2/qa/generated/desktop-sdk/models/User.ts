/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type User = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
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

