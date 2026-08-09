/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ResolvedLocation = {
    catalogItemId: string;
    locationId: string;
    displayPath: string;
    rawPath?: string | null;
    rawPathVisible: boolean;
    availability: 'unknown' | 'available' | 'not_found' | 'access_denied' | 'resource_unavailable';
    version: number;
};

