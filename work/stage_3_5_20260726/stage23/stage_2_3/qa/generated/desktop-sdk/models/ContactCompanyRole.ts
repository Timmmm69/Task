/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ContactCompanyRole = {
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
    contactId: string;
    companyId: string;
    jobTitle?: string | null;
    departmentName?: string | null;
    isPrimary: boolean;
    /**
     * Calendar date without a time zone.
     */
    validFrom?: string | null;
    /**
     * Calendar date without a time zone.
     */
    validTo?: string | null;
};

