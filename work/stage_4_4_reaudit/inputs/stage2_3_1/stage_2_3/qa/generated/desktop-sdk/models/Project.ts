/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type Project = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    readonly createdAt?: string;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    readonly updatedAt?: string;
    name: string;
    description?: string | null;
    ownerUserId: string;
    managerUserId?: string | null;
    status: 'planning' | 'active' | 'paused' | 'completed';
    /**
     * Calendar date without a time zone.
     */
    startDate?: string | null;
    /**
     * Calendar date without a time zone.
     */
    plannedEndDate?: string | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    actualEndAt?: string | null;
    defaultTimeZone?: string | null;
    colorCode?: string | null;
};

