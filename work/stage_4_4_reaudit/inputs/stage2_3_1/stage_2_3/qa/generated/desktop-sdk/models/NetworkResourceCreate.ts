/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type NetworkResourceCreate = {
    name: string;
    rootUncPath: string;
    status?: 'active' | 'degraded' | 'unavailable' | 'retired';
    allowWriteMetadata?: boolean;
};

