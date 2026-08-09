/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type Company = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    name: string;
    legalName?: string | null;
    industry?: string | null;
    website?: string | null;
    taxIdentifier?: string | null;
    notes?: string | null;
    status: 'active' | 'inactive';
};

