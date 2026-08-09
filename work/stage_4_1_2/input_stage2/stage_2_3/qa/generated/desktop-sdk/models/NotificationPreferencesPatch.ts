/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type NotificationPreferencesPatch = {
    enabled?: boolean;
    desktopEnabled?: boolean;
    soundEnabled?: boolean;
    defaultSnoozeMinutes?: number;
    /**
     * Local wall-clock time. Interpret only together with the companion IANA time-zone field.
     */
    quietHoursStart?: string | null;
    /**
     * Local wall-clock time. Interpret only together with the companion IANA time-zone field.
     */
    quietHoursEnd?: string | null;
    quietHoursTimeZone?: string | null;
};

