/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type AuditEntry = {
    id: string;
    occurredAt: string;
    actorUserId?: string | null;
    actionCode: string;
    objectId?: string | null;
    outcome: 'success' | 'denied' | 'failure';
    reasonCode?: string | null;
    correlationId: string;
};

