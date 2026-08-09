/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ContactAttendee = {
    contactId: string;
    role: 'required' | 'optional' | 'observer';
    responseStatus: 'pending' | 'accepted' | 'declined' | 'tentative';
    respondedAt?: string | null;
};

