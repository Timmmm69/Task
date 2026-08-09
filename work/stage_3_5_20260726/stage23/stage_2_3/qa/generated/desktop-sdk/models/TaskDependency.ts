/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type TaskDependency = {
    predecessorTaskId: string;
    successorTaskId: string;
    dependencyType: 'finish_to_start' | 'start_to_start';
};

