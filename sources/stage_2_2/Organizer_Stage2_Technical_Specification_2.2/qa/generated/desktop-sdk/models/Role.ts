/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type Role = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    code: string;
    name: string;
    scopeType: 'organization' | 'department';
    readonly isSystem: boolean;
    status: 'active' | 'inactive';
    permissionCodes: Array<string>;
};

