/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type TaskTransitionRequest = {
    targetStatus: 'new' | 'in_progress' | 'review' | 'completed' | 'cancelled';
    reason?: string | null;
};

