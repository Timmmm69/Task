/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { SearchSuggestion } from './SearchSuggestion';
export type SearchPage = {
    items: Array<SearchSuggestion>;
    nextCursor?: string | null;
    tookMs: number;
};

