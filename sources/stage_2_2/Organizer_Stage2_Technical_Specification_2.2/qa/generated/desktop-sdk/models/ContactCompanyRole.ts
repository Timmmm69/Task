/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ContactCompanyRole = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    contactId: string;
    companyId: string;
    jobTitle?: string | null;
    departmentName?: string | null;
    isPrimary: boolean;
    validFrom?: string | null;
    validTo?: string | null;
};

