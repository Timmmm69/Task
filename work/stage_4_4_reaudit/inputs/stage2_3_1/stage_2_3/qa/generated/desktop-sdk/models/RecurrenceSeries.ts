/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RecurrenceTaskTemplate } from './RecurrenceTaskTemplate';
export type RecurrenceSeries = {
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
    status: 'active' | 'paused' | 'completed' | 'cancelled';
    frequency: 'daily' | 'weekly' | 'monthly' | 'yearly';
    interval: number;
    weekdays?: Array<number>;
    monthDays?: Array<number>;
    monthOfYear?: number | null;
    /**
     * Calendar date without a time zone.
     */
    occurrenceStartDate: string;
    /**
     * Local wall-clock time. Interpret only together with the companion IANA time-zone field.
     */
    localStartTime?: string | null;
    timeZone: string;
    /**
     * Calendar date without a time zone.
     */
    untilDate?: string | null;
    maxOccurrences?: number | null;
    /**
     * Calendar date without a time zone.
     */
    nextGenerationDate: string;
    template: RecurrenceTaskTemplate;
};

