/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { EmployeeSearchResult } from './EmployeeSearchResult';
import type { ObjectReference } from './ObjectReference';
export type SearchSuggestion = {
    object: ObjectReference;
    matchedField: string;
    highlight: string;
    score: number;
    resultType?: 'object' | 'employee';
    employee?: (EmployeeSearchResult | null);
};

