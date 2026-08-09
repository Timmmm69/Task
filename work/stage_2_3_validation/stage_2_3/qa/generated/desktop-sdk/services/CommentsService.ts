/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { Comment } from '../models/Comment';
import type { CommentCreate } from '../models/CommentCreate';
import type { CommentPage } from '../models/CommentPage';
import type { CommentPatch } from '../models/CommentPatch';
import type { CommentVersionPage } from '../models/CommentVersionPage';
import type { DeletionReceipt } from '../models/DeletionReceipt';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class CommentsService {
    /**
     * Восстановить удалённый комментарий
     * @returns Comment Successful response.
     * @throws ApiError
     */
    public static postApiV1CommentsIdRestore({
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
    }): CancelablePromise<Comment> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/comments/{id}/restore',
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
     * Версии комментария
     * @returns CommentVersionPage Successful response.
     * @throws ApiError
     */
    public static getApiV1CommentsIdVersions({
        id,
        xCorrelationId,
        page,
    }: {
        id: string,
        xCorrelationId?: string,
        page?: number,
    }): CancelablePromise<CommentVersionPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/comments/{id}/versions',
            path: {
                'id': id,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'page': page,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                404: `Resource is absent or hidden by authorization scope.`,
            },
        });
    }
    /**
     * Список комментарий
     * @returns CommentPage Successful response.
     * @throws ApiError
     */
    public static getApiV1ObjectsObjectIdComments({
        objectId,
        xCorrelationId,
        filter,
        sort,
        page,
        cursor,
    }: {
        objectId: string,
        xCorrelationId?: string,
        filter?: string,
        sort?: string,
        page?: number,
        /**
         * Opaque cursor bound to normalized filters, stable sort, authorization scope version and search-index snapshot. Reusing it with different filters is invalid.
         */
        cursor?: string,
    }): CancelablePromise<CommentPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/objects/{objectId}/comments',
            path: {
                'objectId': objectId,
            },
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
     * Создать комментарий
     * @returns Comment Resource created.
     * @throws ApiError
     */
    public static postApiV1ObjectsObjectIdComments({
        objectId,
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        objectId: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: CommentCreate,
        xCorrelationId?: string,
    }): CancelablePromise<Comment> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/objects/{objectId}/comments',
            path: {
                'objectId': objectId,
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
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
                422: `Syntactically valid request violates field or domain invariants.`,
            },
        });
    }
    /**
     * Переместить комментарий в корзину
     * @returns DeletionReceipt Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static deleteApiV1ObjectsObjectIdCommentsId({
        objectId,
        id,
        ifMatch,
        xCorrelationId,
    }: {
        objectId: string,
        id: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        xCorrelationId?: string,
    }): CancelablePromise<DeletionReceipt> {
        return __request(OpenAPI, {
            method: 'DELETE',
            url: '/api/v1/objects/{objectId}/comments/{id}',
            path: {
                'objectId': objectId,
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
     * Получить комментарий
     * @returns Comment Successful response.
     * @throws ApiError
     */
    public static getApiV1ObjectsObjectIdCommentsId({
        objectId,
        id,
        xCorrelationId,
    }: {
        objectId: string,
        id: string,
        xCorrelationId?: string,
    }): CancelablePromise<Comment> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/objects/{objectId}/comments/{id}',
            path: {
                'objectId': objectId,
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
     * Изменить комментарий
     * @returns Comment Successful response.
     * @throws ApiError
     */
    public static patchApiV1ObjectsObjectIdCommentsId({
        objectId,
        id,
        ifMatch,
        requestBody,
        xCorrelationId,
    }: {
        objectId: string,
        id: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        requestBody: CommentPatch,
        xCorrelationId?: string,
    }): CancelablePromise<Comment> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/objects/{objectId}/comments/{id}',
            path: {
                'objectId': objectId,
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
}
