/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ProjectPatch = {
    name?: string;
    description?: string | null;
    ownerUserId?: string;
    managerUserId?: string | null;
    status?: 'planning' | 'active' | 'paused' | 'completed';
    startDate?: string | null;
    plannedEndDate?: string | null;
    actualEndAt?: string | null;
    defaultTimeZone?: string | null;
    colorCode?: string | null;
};

