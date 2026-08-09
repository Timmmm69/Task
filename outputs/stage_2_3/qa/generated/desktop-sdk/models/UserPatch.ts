/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
/**
 * PATCH semantics: omitted properties remain unchanged; an explicit null clears only nullable properties; readOnly properties are rejected; at least one writable property is required.
 */
export type UserPatch = {
    displayName?: string;
    firstName?: string;
    lastName?: string;
    login?: string;
    workEmail?: string | null;
    departmentId?: string | null;
    jobTitle?: string | null;
    accountStatus?: 'pending_activation' | 'active' | 'blocked' | 'deactivated';
};

