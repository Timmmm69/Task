/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
/**
 * PATCH semantics: omitted properties remain unchanged; an explicit null clears only nullable properties; readOnly properties are rejected; at least one writable property is required.
 */
export type ProjectMemberPatch = {
    projectId?: string;
    userAccountId?: string;
    projectRoleId?: string;
    status?: 'invited' | 'active' | 'removed';
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    joinedAt?: string | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    removedAt?: string | null;
    expectedProjectVersion?: number;
};

