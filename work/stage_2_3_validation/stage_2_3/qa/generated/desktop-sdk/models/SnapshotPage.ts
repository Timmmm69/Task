/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { SnapshotItem } from './SnapshotItem';
export type SnapshotPage = {
    mode: 'snapshot';
    snapshotSessionId: string;
    cutSequence: number;
    scopeVersion: number;
    dataset: string;
    items: Array<SnapshotItem>;
    nextDataset?: string | null;
    nextOrdinal?: number | null;
    snapshotComplete: boolean;
    catchUpFromSequence: number;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    expiresAt: string;
};

