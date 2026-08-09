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
    expiresAt: string;
};

