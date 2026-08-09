/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { FileLocationDeviceState } from './FileLocationDeviceState';
export type FileLocationCreate = {
    catalogItemId?: string;
    locationType: 'local_path' | 'unc_path' | 'mapped_drive';
    rawPath: string;
    deviceId?: string | null;
    networkResourceId?: string | null;
    priority?: number;
    isEnabled?: boolean;
    isPrimary?: boolean;
    deviceAvailability?: Array<FileLocationDeviceState>;
};

