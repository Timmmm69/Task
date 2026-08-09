/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type Interaction = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    counterpartyObjectId: string;
    interactionType: 'call' | 'meeting' | 'email' | 'agreement' | 'note' | 'next_step';
    occurredAt: string;
    subject: string;
    details?: string | null;
    nextStep?: string | null;
    nextStepDueAt?: string | null;
    participantObjectIds: Array<string>;
};

