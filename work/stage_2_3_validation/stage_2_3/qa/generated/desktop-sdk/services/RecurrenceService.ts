/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { GenerateOccurrencesRequest } from '../models/GenerateOccurrencesRequest';
import type { GenerationSummary } from '../models/GenerationSummary';
import type { OccurrencePreview } from '../models/OccurrencePreview';
import type { RecurrenceChangeResult } from '../models/RecurrenceChangeResult';
import type { RecurrenceOccurrence } from '../models/RecurrenceOccurrence';
import type { RecurrencePreviewRequest } from '../models/RecurrencePreviewRequest';
import type { RecurrenceScopedChange } from '../models/RecurrenceScopedChange';
import type { RecurrenceSeries } from '../models/RecurrenceSeries';
import type { RecurrenceSeriesCreate } from '../models/RecurrenceSeriesCreate';
import type { RecurrenceSeriesPage } from '../models/RecurrenceSeriesPage';
import type { RecurrenceSeriesPatch } from '../models/RecurrenceSeriesPatch';
import type { RestoreRequest } from '../models/RestoreRequest';
import type { SkipOccurrenceRequest } from '../models/SkipOccurrenceRequest';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class RecurrenceService {
    /**
     * Список серию повторений
     * @returns RecurrenceSeriesPage Successful response.
     * @throws ApiError
     */
    public static getApiV1RecurrenceSeries({
        xCorrelationId,
        filter,
        sort,
        page,
        cursor,
    }: {
        xCorrelationId?: string,
        filter?: string,
        sort?: string,
        page?: number,
        /**
         * Opaque cursor bound to normalized filters, stable sort, authorization scope version and search-index snapshot. Reusing it with different filters is invalid.
         */
        cursor?: string,
    }): CancelablePromise<RecurrenceSeriesPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/recurrence-series',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'filter': filter,
                'sort': sort,
                'page': page,
                'cursor': cursor,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
            },
        });
    }
    /**
     * Создать серию повторений
     * @returns RecurrenceSeries Resource created.
     * @throws ApiError
     */
    public static postApiV1RecurrenceSeries({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: RecurrenceSeriesCreate,
        xCorrelationId?: string,
    }): CancelablePromise<RecurrenceSeries> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/recurrence-series',
            headers: {
                'X-Correlation-ID': xCorrelationId,
                'Idempotency-Key': idempotencyKey,
            },
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
                422: `Syntactically valid request violates field or domain invariants.`,
            },
        });
    }
    /**
     * Отменить серию без помещения в универсальную корзину
     * @returns RecurrenceSeries Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static deleteApiV1RecurrenceSeriesId({
        id,
        ifMatch,
        xCorrelationId,
    }: {
        id: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        xCorrelationId?: string,
    }): CancelablePromise<RecurrenceSeries> {
        return __request(OpenAPI, {
            method: 'DELETE',
            url: '/api/v1/recurrence-series/{id}',
            path: {
                'id': id,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
                'If-Match': ifMatch,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                404: `Resource is absent or hidden by authorization scope.`,
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
                412: `If-Match does not match the current aggregate version.`,
                428: `If-Match is required for this operation.`,
            },
        });
    }
    /**
     * Получить серию повторений
     * @returns RecurrenceSeries Successful response.
     * @throws ApiError
     */
    public static getApiV1RecurrenceSeriesId({
        id,
        xCorrelationId,
    }: {
        id: string,
        xCorrelationId?: string,
    }): CancelablePromise<RecurrenceSeries> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/recurrence-series/{id}',
            path: {
                'id': id,
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
    /**
     * Изменить серию повторений
     * @returns RecurrenceSeries Successful response.
     * @throws ApiError
     */
    public static patchApiV1RecurrenceSeriesId({
        id,
        ifMatch,
        requestBody,
        xCorrelationId,
    }: {
        id: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        requestBody: RecurrenceSeriesPatch,
        xCorrelationId?: string,
    }): CancelablePromise<RecurrenceSeries> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/recurrence-series/{id}',
            path: {
                'id': id,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
                'If-Match': ifMatch,
            },
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                404: `Resource is absent or hidden by authorization scope.`,
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
                412: `If-Match does not match the current aggregate version.`,
                422: `Syntactically valid request violates field or domain invariants.`,
                428: `If-Match is required for this operation.`,
            },
        });
    }
    /**
     * Изменить one/future/all
     * @returns RecurrenceChangeResult Successful response.
     * @throws ApiError
     */
    public static postApiV1RecurrenceSeriesIdApplyChange({
        id,
        idempotencyKey,
        ifMatch,
        requestBody,
        xCorrelationId,
    }: {
        id: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        requestBody: RecurrenceScopedChange,
        xCorrelationId?: string,
    }): CancelablePromise<RecurrenceChangeResult> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/recurrence-series/{id}/apply-change',
            path: {
                'id': id,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
                'Idempotency-Key': idempotencyKey,
                'If-Match': ifMatch,
            },
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                404: `Resource is absent or hidden by authorization scope.`,
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
                412: `If-Match does not match the current aggregate version.`,
                422: `Syntactically valid request violates field or domain invariants.`,
                428: `If-Match is required for this operation.`,
            },
        });
    }
    /**
     * Административно расширить горизонт
     * @returns GenerationSummary Successful response.
     * @throws ApiError
     */
    public static postApiV1RecurrenceSeriesIdGenerate({
        id,
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        id: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: GenerateOccurrencesRequest,
        xCorrelationId?: string,
    }): CancelablePromise<GenerationSummary> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/recurrence-series/{id}/generate',
            path: {
                'id': id,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
                'Idempotency-Key': idempotencyKey,
            },
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                404: `Resource is absent or hidden by authorization scope.`,
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
            },
        });
    }
    /**
     * Предпросмотр будущих occurrence
     * @returns OccurrencePreview Successful response.
     * @throws ApiError
     */
    public static postApiV1RecurrenceSeriesIdPreview({
        id,
        requestBody,
        xCorrelationId,
    }: {
        id: string,
        requestBody: RecurrencePreviewRequest,
        xCorrelationId?: string,
    }): CancelablePromise<Array<OccurrencePreview>> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/recurrence-series/{id}/preview',
            path: {
                'id': id,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                404: `Resource is absent or hidden by authorization scope.`,
                422: `Syntactically valid request violates field or domain invariants.`,
            },
        });
    }
    /**
     * Возобновить приостановленную серию
     * @returns RecurrenceSeries Successful response.
     * @throws ApiError
     */
    public static postApiV1RecurrenceSeriesIdResume({
        id,
        idempotencyKey,
        ifMatch,
        requestBody,
        xCorrelationId,
    }: {
        id: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        requestBody: RestoreRequest,
        xCorrelationId?: string,
    }): CancelablePromise<RecurrenceSeries> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/recurrence-series/{id}/resume',
            path: {
                'id': id,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
                'Idempotency-Key': idempotencyKey,
                'If-Match': ifMatch,
            },
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                404: `Resource is absent or hidden by authorization scope.`,
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
                412: `If-Match does not match the current aggregate version.`,
                422: `Syntactically valid request violates field or domain invariants.`,
                428: `If-Match is required for this operation.`,
            },
        });
    }
    /**
     * Пропустить экземпляр
     * @returns RecurrenceOccurrence Successful response.
     * @throws ApiError
     */
    public static postApiV1RecurrenceSeriesIdSkipOccurrenceKey({
        id,
        occurrenceKey,
        idempotencyKey,
        ifMatch,
        requestBody,
        xCorrelationId,
    }: {
        id: string,
        occurrenceKey: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        requestBody: SkipOccurrenceRequest,
        xCorrelationId?: string,
    }): CancelablePromise<RecurrenceOccurrence> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/recurrence-series/{id}/skip/{occurrenceKey}',
            path: {
                'id': id,
                'occurrenceKey': occurrenceKey,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
                'Idempotency-Key': idempotencyKey,
                'If-Match': ifMatch,
            },
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                404: `Resource is absent or hidden by authorization scope.`,
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
                412: `If-Match does not match the current aggregate version.`,
                428: `If-Match is required for this operation.`,
            },
        });
    }
}
