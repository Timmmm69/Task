/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type UserSettings = {
    language: string;
    timeFormat: '12h' | '24h';
    firstDayOfWeek: number;
    workdayStart: string;
    workdayEnd: string;
    weekendDays: Array<number>;
    defaultTaskDurationMinutes: number;
    defaultReminderOffsetMinutes: number;
    autostartEnabled: boolean;
    allowLocalPaths: boolean;
    confirmCatalogDelete: boolean;
    missingFileBehavior: 'show_actions' | 'keep_inactive' | 'prompt_relink';
    version: number;
};

