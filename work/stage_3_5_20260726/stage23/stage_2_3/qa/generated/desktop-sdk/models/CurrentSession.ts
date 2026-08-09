/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { Device } from './Device';
import type { User } from './User';
export type CurrentSession = {
    sessionId: string;
    organizationId: string;
    user: User;
    device: Device;
    permissionCodes: Array<string>;
    capabilities: Array<string>;
    scopeVersion: number;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    accessExpiresAt: string;
};

