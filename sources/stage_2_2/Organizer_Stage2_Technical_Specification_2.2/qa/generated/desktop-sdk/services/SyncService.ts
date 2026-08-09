/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { RealtimeNegotiation } from '../models/RealtimeNegotiation';
import type { SyncAck } from '../models/SyncAck';
import type { SyncBatch } from '../models/SyncBatch';
import type { SyncBootstrapRequest } from '../models/SyncBootstrapRequest';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class SyncService {
    /**
     * Параметры WebSocket/realtime
     * @returns RealtimeNegotiation Successful response.
     * @throws ApiError
     */
    public static getApiV1RealtimeNegotiate({
        xCorrelationId,
        cursor,
    }: {
        xCorrelationId?: string,
        cursor?: string,
    }): CancelablePromise<RealtimeNegotiation> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/realtime/negotiate',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'cursor': cursor,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
            },
        });
    }
    /**
     * Подтвердить применённый cursor
     * @returns void
     * @throws ApiError
     */
    public static postApiV1SyncAck({
        ifMatch,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        requestBody: SyncAck,
        xCorrelationId?: string,
    }): CancelablePromise<void> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/sync/ack',
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
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
                412: `If-Match does not match the current aggregate version.`,
                428: `If-Match is required for this operation.`,
            },
        });
    }
    /**
     * Начальная авторизационная синхронизация
     * @returns SyncBatch Successful response.
     * @throws ApiError
     */
    public static postApiV1SyncBootstrap({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: SyncBootstrapRequest,
        xCorrelationId?: string,
    }): CancelablePromise<SyncBatch> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/sync/bootstrap',
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
                413: `Request exceeds the configured size limit.`,
                422: `Syntactically valid request violates field or domain invariants.`,
            },
        });
    }
    /**
     * Изменения после cursor
     * @returns SyncBatch Successful response.
     * @throws ApiError
     */
    public static getApiV1SyncChanges({
        xCorrelationId,
        cursor,
        limit,
        scopeVersion,
    }: {
        xCorrelationId?: string,
        cursor?: string,
        limit?: number,
        scopeVersion?: number,
    }): CancelablePromise<SyncBatch> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/sync/changes',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'cursor': cursor,
                'limit': limit,
                'scopeVersion': scopeVersion,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
                410: `Sync cursor or retained resource has expired.`,
                422: `Syntactically valid request violates field or domain invariants.`,
            },
        });
    }
}
