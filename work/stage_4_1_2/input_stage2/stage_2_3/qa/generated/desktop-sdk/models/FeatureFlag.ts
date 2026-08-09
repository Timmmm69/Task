/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type FeatureFlag = {
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
    key: string;
    enabled: boolean;
    minimumClientVersion?: string | null;
    /**
     * Flag-specific validated configuration.
     */
    configuration: Record<string, (string | number | boolean | Array<string>)>;
};

