/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ObjectReference } from './ObjectReference';
export type ArchiveEntry = {
    object: ObjectReference;
    archivedBy: string;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    archivedAt: string;
    reason?: string | null;
    status: 'archived' | 'restored';
};

