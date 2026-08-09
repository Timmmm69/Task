/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type RestorePlan = {
    id: string;
    backupRunId: string;
    status: 'draft' | 'validated' | 'approved' | 'executing' | 'completed' | 'failed';
    steps: Array<string>;
    expiresAt: string;
};

