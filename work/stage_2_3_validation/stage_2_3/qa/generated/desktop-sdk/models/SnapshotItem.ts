/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { CalendarEvent } from './CalendarEvent';
import type { CatalogItem } from './CatalogItem';
import type { Checklist } from './Checklist';
import type { Comment } from './Comment';
import type { Company } from './Company';
import type { Contact } from './Contact';
import type { Department } from './Department';
import type { NetworkResource } from './NetworkResource';
import type { Notification } from './Notification';
import type { Project } from './Project';
import type { ProjectMember } from './ProjectMember';
import type { RecurrenceSeries } from './RecurrenceSeries';
import type { Reminder } from './Reminder';
import type { Tag } from './Tag';
import type { Task } from './Task';
import type { User } from './User';
export type SnapshotItem = {
    ordinal: number;
    objectId: string;
    objectType: string;
    objectVersion: number;
    payload: (User | Department | Project | ProjectMember | Task | Checklist | RecurrenceSeries | CalendarEvent | Reminder | Notification | Comment | Contact | Company | CatalogItem | NetworkResource | Tag);
};

