/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type AuditEntry = {
    id: string;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    occurredAt: string;
    actorUserId?: string | null;
    actionCode: string;
    objectId?: string | null;
    outcome: 'success' | 'denied' | 'failure';
    reasonCode?: string | null;
    correlationId: string;
};

