/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RecurrenceTaskTemplate } from './RecurrenceTaskTemplate';
export type RecurrenceSeriesPatch = {
    status?: 'active' | 'paused' | 'completed' | 'cancelled';
    frequency?: 'daily' | 'weekly' | 'monthly' | 'yearly';
    interval?: number;
    weekdays?: Array<number>;
    monthDays?: Array<number>;
    monthOfYear?: number | null;
    occurrenceStartDate?: string;
    localStartTime?: string | null;
    timeZone?: string;
    untilDate?: string | null;
    maxOccurrences?: number | null;
    nextGenerationDate?: string;
    template?: RecurrenceTaskTemplate;
};

