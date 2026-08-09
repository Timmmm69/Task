/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ChecklistItem } from './ChecklistItem';
/**
 * PATCH semantics: omitted properties remain unchanged; an explicit null clears only nullable properties; readOnly properties are rejected; at least one writable property is required.
 */
export type ChecklistPatch = {
    taskId?: string;
    title?: string;
    sortOrder?: number;
    items?: Array<ChecklistItem>;
};

