/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type TaskDependencyCreate = {
    predecessorTaskId: string;
    dependencyType: 'finish_to_start' | 'start_to_start';
    expectedPredecessorVersion: number;
};

