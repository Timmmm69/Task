/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type HistoryEntry = {
    id: string;
    objectId: string;
    objectVersion: number;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    changedAt: string;
    changeType: string;
    changedFields: Array<string>;
    correlationId: string;
};

