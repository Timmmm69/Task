/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type RoleCreate = {
    code: string;
    name: string;
    scopeType: 'organization' | 'department';
    status?: 'active' | 'inactive';
    permissionCodes?: Array<string>;
};

