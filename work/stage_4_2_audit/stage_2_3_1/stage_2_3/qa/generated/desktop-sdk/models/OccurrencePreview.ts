/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type OccurrencePreview = {
    occurrenceKey: string;
    /**
     * Calendar date without a time zone.
     */
    localDate: string;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    startAtUtc?: string | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    deadlineAt?: string | null;
    dstAdjustment: 'none' | 'shifted_forward' | 'earlier_offset' | 'later_offset' | 'skipped';
};

