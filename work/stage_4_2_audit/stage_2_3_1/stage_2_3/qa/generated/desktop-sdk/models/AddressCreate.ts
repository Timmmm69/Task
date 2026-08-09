/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type AddressCreate = {
    ownerObjectId?: string;
    addressType: 'work' | 'legal' | 'postal' | 'other';
    countryCode?: string | null;
    region?: string | null;
    city?: string | null;
    street?: string | null;
    postalCode?: string | null;
    formattedAddress: string;
    isPrimary?: boolean;
};

