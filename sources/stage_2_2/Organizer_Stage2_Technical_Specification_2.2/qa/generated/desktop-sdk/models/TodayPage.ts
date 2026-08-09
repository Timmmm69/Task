/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { TodayItem } from './TodayItem';
export type TodayPage = {
    date: string;
    timeZone: string;
    items: Array<TodayItem>;
    nextCursor?: string | null;
};

