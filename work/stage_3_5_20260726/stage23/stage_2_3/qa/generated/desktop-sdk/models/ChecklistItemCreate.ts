/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ChecklistItemCreate = {
    checklistId?: string;
    text: string;
    isCompleted?: boolean;
    completedBy?: string | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    completedAt?: string | null;
    sortOrder?: number;
};

