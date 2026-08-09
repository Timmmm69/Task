/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type NetworkResource = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    name: string;
    rootUncPath: string;
    status: 'active' | 'degraded' | 'unavailable' | 'retired';
    allowWriteMetadata: boolean;
    readonly lastHealthAt?: string | null;
};

