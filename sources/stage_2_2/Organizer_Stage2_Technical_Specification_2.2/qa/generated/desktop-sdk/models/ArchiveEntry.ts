/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ObjectReference } from './ObjectReference';
export type ArchiveEntry = {
    object: ObjectReference;
    archivedBy: string;
    archivedAt: string;
    reason?: string | null;
    status: 'archived' | 'restored';
};

