/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ObjectReference } from './ObjectReference';
export type TrashEntry = {
    object: ObjectReference;
    deletedBy: string;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    deletedAt: string;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    purgeAfter: string;
    status: 'retained' | 'restored' | 'purged' | 'blocked_by_hold';
};

