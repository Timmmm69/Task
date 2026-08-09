/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
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
    scheduledDate?: string | null;
    startTimeLocal?: string | null;
    scheduleTimeZone?: string | null;
    plannedDurationMinutes?: number | null;
    deadlineAt?: string | null;
    assigneeIds?: Array<string>;
    watcherIds?: Array<string>;
};

