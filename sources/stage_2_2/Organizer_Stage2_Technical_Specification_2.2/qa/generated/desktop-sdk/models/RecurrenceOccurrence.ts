/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type RecurrenceOccurrence = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    seriesId: string;
    occurrenceKey: string;
    localDate: string;
    status: 'planned' | 'generated' | 'skipped' | 'cancelled';
    taskId?: string | null;
};

