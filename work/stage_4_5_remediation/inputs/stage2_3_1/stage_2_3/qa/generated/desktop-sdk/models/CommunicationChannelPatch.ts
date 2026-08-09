/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
/**
 * PATCH semantics: omitted properties remain unchanged; an explicit null clears only nullable properties; readOnly properties are rejected; at least one writable property is required.
 */
export type CommunicationChannelPatch = {
    ownerObjectId?: string;
    channelType?: 'phone' | 'email' | 'telegram' | 'whatsapp' | 'viber' | 'other_messenger' | 'website';
    label?: string | null;
    value?: string;
    isPrimary?: boolean;
    isVerified?: boolean;
};

