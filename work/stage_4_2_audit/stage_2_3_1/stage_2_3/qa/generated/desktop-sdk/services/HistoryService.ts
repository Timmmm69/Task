/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { HistoryEntryPage } from '../models/HistoryEntryPage';
import type { HistoryVersion } from '../models/HistoryVersion';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class HistoryService {
    /**
     * История доступного объекта
     * @returns HistoryEntryPage Successful response.
     * @throws ApiError
     */
    public static getApiV1ObjectsObjectIdHistory({
        objectId,
        xCorrelationId,
        cursor,
        limit,
    }: {
        objectId: string,
        xCorrelationId?: string,
        /**
         * Opaque cursor bound to normalized filters, stable sort, authorization scope version and search-index snapshot. Reusing it with different filters is invalid.
         */
        cursor?: string,
        /**
         * Maximum number of authorization-filtered results returned by the server.
         */
        limit?: number,
    }): CancelablePromise<HistoryEntryPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/objects/{objectId}/history',
            path: {
                'objectId': objectId,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'cursor': cursor,
                'limit': limit,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                404: `Resource is absent or hidden by authorization scope.`,
            },
        });
    }
    /**
     * Снимок/изменения версии
     * @returns HistoryVersion Successful response.
     * @throws ApiError
     */
    public static getApiV1ObjectsObjectIdHistoryVersion({
        objectId,
        version,
        xCorrelationId,
    }: {
        objectId: string,
        version: number,
        xCorrelationId?: string,
    }): CancelablePromise<HistoryVersion> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/objects/{objectId}/history/{version}',
            path: {
                'objectId': objectId,
                'version': version,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                404: `Resource is absent or hidden by authorization scope.`,
            },
        });
    }
}
