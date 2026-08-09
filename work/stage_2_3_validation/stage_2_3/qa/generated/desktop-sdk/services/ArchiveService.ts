/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ArchiveEntryPage } from '../models/ArchiveEntryPage';
import type { ObjectReference } from '../models/ObjectReference';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class ArchiveService {
    /**
     * Архив
     * @returns ArchiveEntryPage Successful response.
     * @throws ApiError
     */
    public static getApiV1Archive({
        xCorrelationId,
        type,
        projectId,
        page,
    }: {
        xCorrelationId?: string,
        type?: string,
        projectId?: string,
        page?: number,
    }): CancelablePromise<ArchiveEntryPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/archive',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'type': type,
                'projectId': projectId,
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
     * Вернуть из архива
     * @returns ObjectReference Successful response.
     * @throws ApiError
     */
    public static postApiV1ArchiveObjectIdRestore({
        objectId,
        ifMatch,
        xCorrelationId,
    }: {
        objectId: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        xCorrelationId?: string,
    }): CancelablePromise<ObjectReference> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/archive/{objectId}/restore',
            path: {
                'objectId': objectId,
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
                422: `Syntactically valid request violates field or domain invariants.`,
                428: `If-Match is required for this operation.`,
            },
        });
    }
}
