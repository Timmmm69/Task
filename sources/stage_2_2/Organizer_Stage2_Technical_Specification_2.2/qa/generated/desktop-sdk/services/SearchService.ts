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
        status,
        from,
        to,
        cursor,
        limit,
    }: {
        xCorrelationId?: string,
        q?: string,
        types?: Array<string>,
        projectIds?: Array<string>,
        userIds?: Array<string>,
        departments?: Array<string>,
        status?: string,
        from?: string,
        to?: string,
        cursor?: string,
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
                'status': status,
                'from': from,
                'to': to,
                'cursor': cursor,
                'limit': limit,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
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
        types?: Array<string>,
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
