/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type Notification = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    recipientUserId: string;
    notificationType: string;
    sourceObjectId?: string | null;
    title: string;
    body: string;
    severity: 'info' | 'warning' | 'critical';
    status: 'pending' | 'delivered' | 'read' | 'dismissed' | 'failed' | 'expired';
    notBefore: string;
    expiresAt?: string | null;
};

