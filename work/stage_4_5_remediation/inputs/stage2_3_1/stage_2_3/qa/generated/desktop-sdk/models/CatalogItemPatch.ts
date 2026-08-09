/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
/**
 * PATCH semantics: omitted properties remain unchanged; an explicit null clears only nullable properties; readOnly properties are rejected; at least one writable property is required.
 */
export type CatalogItemPatch = {
    parentId?: string | null;
    itemType?: 'virtual_folder' | 'file_reference' | 'folder_reference' | 'web_link' | 'text_note';
    name?: string;
    description?: string | null;
    noteContent?: string | null;
    webUrl?: string | null;
    mimeType?: string | null;
    fileExtension?: string | null;
    sortOrder?: number;
};

