/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ChangeRecord = {
    sequence: number;
    sourceEventId: string;
    objectType: string;
    objectId: string;
    operation: 'upsert' | 'tombstone' | 'scope_revoke';
    version: number;
    changedFields: Array<string>;
};

