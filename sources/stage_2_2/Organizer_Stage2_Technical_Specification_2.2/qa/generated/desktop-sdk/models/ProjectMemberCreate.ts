/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ProjectMemberCreate = {
    projectId?: string;
    userAccountId: string;
    projectRoleId: string;
    status?: 'invited' | 'active' | 'removed';
    joinedAt?: string | null;
    removedAt?: string | null;
};

