/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
/**
 * PATCH semantics: omitted properties remain unchanged; an explicit null clears only nullable properties; readOnly properties are rejected; at least one writable property is required.
 */
export type TaskPatch = {
    projectId?: string | null;
    parentTaskId?: string | null;
    title?: string;
    description?: string | null;
    authorUserId?: string;
    requesterUserId?: string | null;
    primaryCounterpartyObjectId?: string | null;
    status?: 'new' | 'in_progress' | 'review' | 'completed' | 'cancelled';
    priority?: 'low' | 'normal' | 'high' | 'critical';
    /**
     * Calendar date without a time zone.
     */
    scheduledDate?: string | null;
    /**
     * Local wall-clock time. Interpret only together with the companion IANA time-zone field.
     */
    startTimeLocal?: string | null;
    scheduleTimeZone?: string | null;
    plannedDurationMinutes?: number | null;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    deadlineAt?: string | null;
    assigneeIds?: Array<string>;
    watcherIds?: Array<string>;
};

