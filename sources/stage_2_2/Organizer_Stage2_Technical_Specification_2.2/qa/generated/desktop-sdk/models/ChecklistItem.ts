/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ChecklistItem = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    checklistId: string;
    text: string;
    isCompleted: boolean;
    completedBy?: string | null;
    completedAt?: string | null;
    sortOrder: number;
};

