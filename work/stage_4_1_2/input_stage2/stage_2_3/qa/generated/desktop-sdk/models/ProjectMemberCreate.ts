/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ProjectMemberCreate = {
    projectId?: string;
    userAccountId: string;
    projectRoleId: string;
    status?: 'invited' | 'active' | 'removed';
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    joinedAt?: string | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    removedAt?: string | null;
};

