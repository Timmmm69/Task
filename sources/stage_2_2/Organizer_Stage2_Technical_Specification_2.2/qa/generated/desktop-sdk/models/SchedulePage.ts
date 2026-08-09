/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ScheduleItem } from './ScheduleItem';
export type SchedulePage = {
    items: Array<ScheduleItem>;
    nextCursor?: string | null;
    rangeStart: string;
    rangeEnd: string;
};

