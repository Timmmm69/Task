/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type RealtimeNegotiation = {
    url: string;
    readonly accessToken: string;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    expiresAt: string;
    protocols: Array<'json' | 'messagepack'>;
};

