/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { UrgencyScaleInterval } from './UrgencyScaleInterval';
/**
 * Exactly one interval for each semantic urgency level; scores cover 0..100 with no gaps or overlap.
 */
export type NotificationUrgencyScalePatch = {
    intervals: Array<UrgencyScaleInterval>;
};

