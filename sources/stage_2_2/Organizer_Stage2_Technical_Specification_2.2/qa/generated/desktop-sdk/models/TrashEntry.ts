/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ObjectReference } from './ObjectReference';
export type TrashEntry = {
    object: ObjectReference;
    deletedBy: string;
    deletedAt: string;
    purgeAfter: string;
    status: 'retained' | 'restored' | 'purged' | 'blocked_by_hold';
};

