/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type CommunicationChannelCreate = {
    ownerObjectId?: string;
    channelType: 'phone' | 'email' | 'telegram' | 'whatsapp' | 'viber' | 'other_messenger' | 'website';
    label?: string | null;
    value: string;
    isPrimary?: boolean;
    isVerified?: boolean;
};

