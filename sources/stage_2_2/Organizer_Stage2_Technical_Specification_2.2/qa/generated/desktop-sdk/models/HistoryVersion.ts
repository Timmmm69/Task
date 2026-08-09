/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type HistoryVersion = {
    objectId: string;
    objectVersion: number;
    changeType: 'created' | 'updated' | 'state_changed' | 'archived' | 'restored' | 'trashed' | 'purged';
    changedAt: string;
    changedBy?: string | null;
    changedFields: Array<string>;
    correlationId: string;
};

