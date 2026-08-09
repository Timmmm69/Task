/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
/**
 * PATCH semantics: omitted properties remain unchanged; an explicit null clears only nullable properties; readOnly properties are rejected; at least one writable property is required.
 */
export type InteractionPatch = {
    counterpartyObjectId?: string;
    interactionType?: 'call' | 'meeting' | 'email' | 'agreement' | 'note' | 'next_step';
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    occurredAt?: string;
    subject?: string;
    details?: string | null;
    nextStep?: string | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    nextStepDueAt?: string | null;
    participantObjectIds?: Array<string>;
};

