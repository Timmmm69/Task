/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
/**
 * PATCH semantics: omitted properties remain unchanged; an explicit null clears only nullable properties; readOnly properties are rejected; at least one writable property is required.
 */
export type InboxItemPatch = {
    itemType?: 'task' | 'note' | 'file_link' | 'web_link' | 'idea' | 'assignment';
    title?: string | null;
    content?: string | null;
    rawUrl?: string | null;
    rawPath?: string | null;
    status?: 'unprocessed' | 'converted' | 'discarded';
};

