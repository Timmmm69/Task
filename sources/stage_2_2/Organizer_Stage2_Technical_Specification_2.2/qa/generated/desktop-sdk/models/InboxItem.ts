/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type InboxItem = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
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

