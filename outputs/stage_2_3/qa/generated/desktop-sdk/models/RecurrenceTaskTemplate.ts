/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RecurrenceTemplateChecklist } from './RecurrenceTemplateChecklist';
import type { RecurrenceTemplateReminderRule } from './RecurrenceTemplateReminderRule';
export type RecurrenceTaskTemplate = {
    projectId?: string | null;
    title: string;
    description?: string | null;
    authorUserId: string;
    requesterUserId?: string | null;
    primaryCounterpartyObjectId?: string | null;
    priority: 'low' | 'normal' | 'high' | 'critical';
    plannedDurationMinutes?: number | null;
    deadlineOffsetMinutes?: number | null;
    assigneeIds: Array<string>;
    watcherIds: Array<string>;
    checklists: Array<RecurrenceTemplateChecklist>;
    reminderRules: Array<RecurrenceTemplateReminderRule>;
    templateVersion: number;
};

