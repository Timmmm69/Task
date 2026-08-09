/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type ContactAttendee = {
    contactId: string;
    role: 'required' | 'optional' | 'observer';
    responseStatus: 'pending' | 'accepted' | 'declined' | 'tentative';
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    respondedAt?: string | null;
};

