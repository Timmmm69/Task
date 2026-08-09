/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { TodayItem } from './TodayItem';
export type TodayPage = {
    /**
     * Calendar date without a time zone.
     */
    date: string;
    timeZone: string;
    items: Array<TodayItem>;
    nextCursor?: string | null;
};

