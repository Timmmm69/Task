/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type BulkTaskTransition = {
    items: Array<{
        taskId: string;
        expectedVersion: number;
        targetStatus: 'new' | 'in_progress' | 'review' | 'completed' | 'cancelled';
    }>;
};

