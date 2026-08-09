/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { FileLocationDeviceState } from './FileLocationDeviceState';
/**
 * PATCH semantics: omitted properties remain unchanged; an explicit null clears only nullable properties; readOnly properties are rejected; at least one writable property is required.
 */
export type FileLocationPatch = {
    catalogItemId?: string;
    locationType?: 'local_path' | 'unc_path' | 'mapped_drive';
    rawPath?: string;
    deviceId?: string | null;
    networkResourceId?: string | null;
    priority?: number;
    isEnabled?: boolean;
    isPrimary?: boolean;
    deviceAvailability?: Array<FileLocationDeviceState>;
    expectedCatalogItemVersion?: number;
};

