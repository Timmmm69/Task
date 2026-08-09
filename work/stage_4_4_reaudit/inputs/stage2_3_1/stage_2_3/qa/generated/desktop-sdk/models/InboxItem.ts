/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type InboxItem = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    readonly createdAt?: string;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    readonly updatedAt?: string;
    readonly ownerUserId: string;
    itemType: 'task' | 'note' | 'file_link' | 'web_link' | 'idea' | 'assignment';
    title?: string | null;
    content?: string | null;
    rawUrl?: string | null;
    rawPath?: string | null;
    status: 'unprocessed' | 'converted' | 'discarded';
    readonly convertedObjectId?: string | null;
};

