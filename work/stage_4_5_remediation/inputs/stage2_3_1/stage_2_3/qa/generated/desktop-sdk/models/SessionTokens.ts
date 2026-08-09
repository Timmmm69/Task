/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type SessionTokens = {
    readonly accessToken: string;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    readonly accessExpiresAt: string;
    readonly refreshToken: string;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    readonly refreshExpiresAt: string;
    readonly sessionId: string;
};

