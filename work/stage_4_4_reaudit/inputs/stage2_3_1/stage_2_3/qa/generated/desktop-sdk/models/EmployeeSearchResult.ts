/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
/**
 * Server-authorized employee result. Redacted fields are null; blocked users are omitted unless the caller has User.Block.
 */
export type EmployeeSearchResult = {
    userId: string;
    displayName: string;
    departmentId?: string | null;
    departmentName?: string | null;
    jobTitle?: string | null;
    accountStatus: 'active' | 'blocked' | 'inactive';
    deepLink: string;
    isRedacted: boolean;
};

