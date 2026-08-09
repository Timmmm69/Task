/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { FileLocationDeviceState } from './FileLocationDeviceState';
export type FileLocation = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    catalogItemId: string;
    locationType: 'local_path' | 'unc_path' | 'mapped_drive';
    readonly displayPath: string;
    rawPath?: string;
    deviceId?: string | null;
    networkResourceId?: string | null;
    readonly ownerUserId: string;
    priority: number;
    isEnabled: boolean;
    isPrimary: boolean;
    deviceAvailability?: Array<FileLocationDeviceState>;
};

