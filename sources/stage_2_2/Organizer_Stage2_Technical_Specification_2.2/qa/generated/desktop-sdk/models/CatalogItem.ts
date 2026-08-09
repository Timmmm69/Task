/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type CatalogItem = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    parentId?: string | null;
    itemType: 'virtual_folder' | 'file_reference' | 'folder_reference' | 'web_link' | 'text_note';
    name: string;
    description?: string | null;
    noteContent?: string | null;
    webUrl?: string | null;
    mimeType?: string | null;
    fileExtension?: string | null;
    sortOrder: number;
};

