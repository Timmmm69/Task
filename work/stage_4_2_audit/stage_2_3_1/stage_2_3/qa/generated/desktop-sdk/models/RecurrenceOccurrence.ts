/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type RecurrenceOccurrence = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    readonly createdAt?: string;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    readonly updatedAt?: string;
    seriesId: string;
    occurrenceKey: string;
    /**
     * Calendar date without a time zone.
     */
    localDate: string;
    status: 'planned' | 'generated' | 'skipped' | 'cancelled';
    taskId?: string | null;
};

