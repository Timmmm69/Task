/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
/**
 * PATCH semantics: omitted properties remain unchanged; an explicit null clears only nullable properties; readOnly properties are rejected; at least one writable property is required.
 */
export type ChecklistItemPatch = {
    checklistId?: string;
    text?: string;
    isCompleted?: boolean;
    completedBy?: string | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    completedAt?: string | null;
    sortOrder?: number;
};

