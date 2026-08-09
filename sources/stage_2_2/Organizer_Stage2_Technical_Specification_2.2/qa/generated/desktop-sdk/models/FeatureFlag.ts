/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type FeatureFlag = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    key: string;
    enabled: boolean;
    minimumClientVersion?: string | null;
    /**
     * Flag-specific validated configuration.
     */
    configuration: Record<string, (string | number | boolean | Array<string>)>;
};

