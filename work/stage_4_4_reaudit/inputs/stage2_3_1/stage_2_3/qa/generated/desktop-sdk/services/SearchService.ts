/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { SearchPage } from '../models/SearchPage';
import type { SearchSuggestion } from '../models/SearchSuggestion';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class SearchService {
    /**
     * Глобальный авторизационный поиск
     * Search is authorized and filtered on the server before cursor pagination. type=employee returns the separate Employees result group; userIds remains only a related-object filter.
     * @returns SearchPage Successful response.
     * @throws ApiError
     */
    public static getApiV1Search({
        xCorrelationId,
        q,
        types,
        projectIds,
        userIds,
        departments,
        contactIds,
        hasFiles,
        lifecycle,
        from,
        to,
        cursor,
        limit,
    }: {
        xCorrelationId?: string,
        q?: string,
        /**
         * Canonical object types to search. Other filters are applied only to compatible types without client-side post-filtering.
         */
        types?: Array<'task' | 'calendar_event' | 'project' | 'catalog_item' | 'file_location' | 'contact' | 'company' | 'interaction' | 'comment' | 'employee'>,
        projectIds?: Array<string>,
        userIds?: Array<string>,
        departments?: Array<string>,
        /**
         * Return objects linked to at least one supplied contact identifier. Applied by the server before pagination.
         */
        contactIds?: Array<string>,
        /**
         * When true, return only objects with at least one accessible file location; when false, only objects without accessible files.
         */
        hasFiles?: boolean,
        /**
         * Cross-type lifecycle filter. active excludes completed, archived and trashed objects; completed selects terminal business items but still excludes archived and trashed objects.
         */
        lifecycle?: Array<'active' | 'completed'>,
        from?: string,
        to?: string,
        /**
         * Opaque cursor bound to normalized filters, stable sort, authorization scope version and search-index snapshot. Reusing it with different filters is invalid.
         */
        cursor?: string,
        /**
         * Maximum number of authorization-filtered results returned by the server.
         */
        limit?: number,
    }): CancelablePromise<SearchPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/search',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'q': q,
                'types': types,
                'projectIds': projectIds,
                'userIds': userIds,
                'departments': departments,
                'contactIds': contactIds,
                'hasFiles': hasFiles,
                'lifecycle': lifecycle,
                'from': from,
                'to': to,
                'cursor': cursor,
                'limit': limit,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                410: `Sync cursor or retained resource has expired.`,
                422: `Syntactically valid request violates field or domain invariants.`,
            },
        });
    }
    /**
     * Подсказки по префиксу
     * @returns SearchSuggestion Successful response.
     * @throws ApiError
     */
    public static getApiV1SearchSuggestions({
        xCorrelationId,
        q,
        types,
        limit,
    }: {
        xCorrelationId?: string,
        q?: string,
        /**
         * Canonical object types to search. Other filters are applied only to compatible types without client-side post-filtering.
         */
        types?: Array<'task' | 'calendar_event' | 'project' | 'catalog_item' | 'file_location' | 'contact' | 'company' | 'interaction' | 'comment'>,
        /**
         * Maximum number of authorization-filtered results returned by the server.
         */
        limit?: number,
    }): CancelablePromise<Array<SearchSuggestion>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/search/suggestions',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'q': q,
                'types': types,
                'limit': limit,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
            },
        });
    }
}
