/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { FieldError } from './FieldError';
export type ProblemDetails = {
    type: string;
    title: string;
    status: number;
    detail?: string | null;
    instance?: string | null;
    code: string;
    traceId: string;
    correlationId: string;
    fieldErrors: Array<FieldError>;
    currentVersion?: number | null;
    currentEtag?: string | null;
    retryAfterSeconds?: number | null;
};

