/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RecurrenceSeriesCreate } from './RecurrenceSeriesCreate';
export type RecurrencePreviewRequest = {
    rule: RecurrenceSeriesCreate;
    /**
     * Calendar date without a time zone.
     */
    fromDate: string;
    limit: number;
};

