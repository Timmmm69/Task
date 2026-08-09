/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type UserSettingsPatch = {
    language?: string;
    timeFormat?: '12h' | '24h';
    firstDayOfWeek?: number;
    /**
     * Local wall-clock time. Interpret only together with the companion IANA time-zone field.
     */
    workdayStart?: string;
    /**
     * Local wall-clock time. Interpret only together with the companion IANA time-zone field.
     */
    workdayEnd?: string;
    weekendDays?: Array<number>;
    defaultTaskDurationMinutes?: number;
    defaultReminderOffsetMinutes?: number;
    autostartEnabled?: boolean;
    allowLocalPaths?: boolean;
    confirmCatalogDelete?: boolean;
    missingFileBehavior?: 'show_actions' | 'keep_inactive' | 'prompt_relink';
};

