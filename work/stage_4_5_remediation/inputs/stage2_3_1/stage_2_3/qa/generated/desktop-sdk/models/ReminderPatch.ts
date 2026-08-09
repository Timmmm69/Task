/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
/**
 * PATCH semantics: omitted properties remain unchanged; an explicit null clears only nullable properties; readOnly properties are rejected; at least one writable property is required.
 */
export type ReminderPatch = {
    targetObjectId?: string;
    recipientUserId?: string;
    triggerType?: 'absolute' | 'before_start' | 'before_deadline' | 'at_start' | 'at_deadline';
    offsetMinutes?: number | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    absoluteTriggerAt?: string | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    nextTriggerAt?: string;
    status?: 'scheduled' | 'due' | 'delivered' | 'snoozed' | 'cancelled' | 'expired';
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    snoozedUntil?: string | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    deliveredAt?: string | null;
};

