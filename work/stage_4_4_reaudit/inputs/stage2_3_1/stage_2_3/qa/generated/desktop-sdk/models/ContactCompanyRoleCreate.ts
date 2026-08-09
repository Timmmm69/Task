/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ContactCompanyRoleCreate = {
    contactId: string;
    companyId: string;
    jobTitle?: string | null;
    departmentName?: string | null;
    isPrimary?: boolean;
    /**
     * Calendar date without a time zone.
     */
    validFrom?: string | null;
    /**
     * Calendar date without a time zone.
     */
    validTo?: string | null;
};

