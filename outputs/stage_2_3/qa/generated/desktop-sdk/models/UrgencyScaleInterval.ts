/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { UrgencyLevel } from './UrgencyLevel';
/**
 * Inclusive score interval. Intervals are ordered, contiguous, and non-overlapping from 0 through 100.
 */
export type UrgencyScaleInterval = {
    urgencyLevel: UrgencyLevel;
    minScore: number;
    maxScore: number;
    displayToken: string;
};

