/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ChangeRecord } from './ChangeRecord';
export type IncrementalSyncBatch = {
    mode: 'incremental';
    fromSequence: number;
    toSequence: number;
    scopeVersion: number;
    changes: Array<ChangeRecord>;
    hasMore: boolean;
    nextCursor?: string | null;
};

