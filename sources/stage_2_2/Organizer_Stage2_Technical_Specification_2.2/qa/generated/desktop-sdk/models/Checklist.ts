/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ChecklistItem } from './ChecklistItem';
export type Checklist = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    taskId: string;
    title: string;
    sortOrder: number;
    items: Array<ChecklistItem>;
};

