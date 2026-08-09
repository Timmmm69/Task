/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type BackgroundJob = {
    id: string;
    jobCode: string;
    scheduleKind: 'cron' | 'interval' | 'event' | 'continuous';
    scheduleExpression?: string | null;
    enabled: boolean;
    maxParallelism: number;
    maxAttempts: number;
    timeoutSeconds: number;
    version: number;
};

