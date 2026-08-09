/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RecurrenceTaskTemplate } from './RecurrenceTaskTemplate';
/**
 * PATCH semantics: omitted properties remain unchanged; an explicit null clears only nullable properties; readOnly properties are rejected; at least one writable property is required.
 */
export type RecurrenceSeriesPatch = {
    status?: 'active' | 'paused' | 'completed' | 'cancelled';
    frequency?: 'daily' | 'weekly' | 'monthly' | 'yearly';
    interval?: number;
    weekdays?: Array<number>;
    monthDays?: Array<number>;
    monthOfYear?: number | null;
    /**
     * Calendar date without a time zone.
     */
    occurrenceStartDate?: string;
    /**
     * Local wall-clock time. Interpret only together with the companion IANA time-zone field.
     */
    localStartTime?: string | null;
    timeZone?: string;
    /**
     * Calendar date without a time zone.
     */
    untilDate?: string | null;
    maxOccurrences?: number | null;
    /**
     * Calendar date without a time zone.
     */
    nextGenerationDate?: string;
    template?: RecurrenceTaskTemplate;
};

