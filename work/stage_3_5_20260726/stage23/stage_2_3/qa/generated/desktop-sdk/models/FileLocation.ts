/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { FileLocationDeviceState } from './FileLocationDeviceState';
export type FileLocation = {
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
    catalogItemId: string;
    locationType: 'local_path' | 'unc_path' | 'mapped_drive';
    readonly displayPath: string;
    readonly rawPath?: string | null;
    deviceId?: string | null;
    networkResourceId?: string | null;
    readonly ownerUserId: string;
    priority: number;
    isEnabled: boolean;
    isPrimary: boolean;
    readonly deviceAvailability?: Array<FileLocationDeviceState>;
    readonly redactedFields: Array<'rawPath'>;
};

