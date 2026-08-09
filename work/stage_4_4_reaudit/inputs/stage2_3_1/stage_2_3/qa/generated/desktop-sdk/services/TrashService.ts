/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { BackgroundJobRun } from '../models/BackgroundJobRun';
import type { ObjectReference } from '../models/ObjectReference';
import type { PurgeRequest } from '../models/PurgeRequest';
import type { RestoreRequest } from '../models/RestoreRequest';
import type { TrashEntryPage } from '../models/TrashEntryPage';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class TrashService {
    /**
     * Корзина
     * @returns TrashEntryPage Successful response.
     * @throws ApiError
     */
    public static getApiV1Trash({
        xCorrelationId,
        type,
        deletedBy,
        purgeBefore,
        page,
    }: {
        xCorrelationId?: string,
        type?: string,
        deletedBy?: string,
        purgeBefore?: string,
        page?: number,
    }): CancelablePromise<TrashEntryPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/trash',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'type': type,
                'deletedBy': deletedBy,
                'purgeBefore': purgeBefore,
                'page': page,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
            },
        });
    }
    /**
     * Purge объекта после retention
     * @returns BackgroundJobRun Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static deleteApiV1TrashObjectId({
        objectId,
        idempotencyKey,
        ifMatch,
        requestBody,
        xCorrelationId,
    }: {
        objectId: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        requestBody: PurgeRequest,
        xCorrelationId?: string,
    }): CancelablePromise<BackgroundJobRun> {
        return __request(OpenAPI, {
            method: 'DELETE',
            url: '/api/v1/trash/{objectId}',
            path: {
                'objectId': objectId,
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
     * Восстановить универсальный объект
     * @returns ObjectReference Successful response.
     * @throws ApiError
     */
    public static postApiV1TrashObjectIdRestore({
        objectId,
        idempotencyKey,
        ifMatch,
        requestBody,
        xCorrelationId,
    }: {
        objectId: string,
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
    }): CancelablePromise<ObjectReference> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/trash/{objectId}/restore',
            path: {
                'objectId': objectId,
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
}
