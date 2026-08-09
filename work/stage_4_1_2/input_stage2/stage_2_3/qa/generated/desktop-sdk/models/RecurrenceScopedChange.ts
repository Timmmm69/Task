/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { TaskPatch } from './TaskPatch';
export type RecurrenceScopedChange = {
    scope: 'this_occurrence' | 'this_and_future' | 'entire_series';
    patch: TaskPatch;
    expectedTaskVersion: number;
};

