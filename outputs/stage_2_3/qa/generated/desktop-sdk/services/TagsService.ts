/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { DeletionReceipt } from '../models/DeletionReceipt';
import type { ObjectLink } from '../models/ObjectLink';
import type { ObjectLinkCreate } from '../models/ObjectLinkCreate';
import type { ObjectLinkPage } from '../models/ObjectLinkPage';
import type { RestoreRequest } from '../models/RestoreRequest';
import type { Tag } from '../models/Tag';
import type { TagCreate } from '../models/TagCreate';
import type { TagIds } from '../models/TagIds';
import type { TagPage } from '../models/TagPage';
import type { TagPatch } from '../models/TagPatch';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class TagsService {
    /**
     * Связи объекта
     * @returns ObjectLinkPage Successful response.
     * @throws ApiError
     */
    public static getApiV1ObjectsObjectIdLinks({
        objectId,
        xCorrelationId,
        type,
        page,
    }: {
        objectId: string,
        xCorrelationId?: string,
        type?: string,
        page?: number,
    }): CancelablePromise<ObjectLinkPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/objects/{objectId}/links',
            path: {
                'objectId': objectId,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'type': type,
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
     * Создать связь объектов
     * @returns ObjectLink Resource created.
     * @throws ApiError
     */
    public static postApiV1ObjectsObjectIdLinks({
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
        requestBody: ObjectLinkCreate,
        xCorrelationId?: string,
    }): CancelablePromise<ObjectLink> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/objects/{objectId}/links',
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
     * Удалить связь объектов
     * @returns void
     * @throws ApiError
     */
    public static deleteApiV1ObjectsObjectIdLinksLinkId({
        objectId,
        linkId,
        ifMatch,
        xCorrelationId,
    }: {
        objectId: string,
        linkId: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        xCorrelationId?: string,
    }): CancelablePromise<void> {
        return __request(OpenAPI, {
            method: 'DELETE',
            url: '/api/v1/objects/{objectId}/links/{linkId}',
            path: {
                'objectId': objectId,
                'linkId': linkId,
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
     * Заменить теги объекта
     * @returns Tag Successful response.
     * @throws ApiError
     */
    public static putApiV1ObjectsObjectIdTags({
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
        requestBody: TagIds,
        xCorrelationId?: string,
    }): CancelablePromise<Array<Tag>> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/objects/{objectId}/tags',
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
     * Список тег
     * @returns TagPage Successful response.
     * @throws ApiError
     */
    public static getApiV1Tags({
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
    }): CancelablePromise<TagPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/tags',
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
     * Создать тег
     * @returns Tag Resource created.
     * @throws ApiError
     */
    public static postApiV1Tags({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: TagCreate,
        xCorrelationId?: string,
    }): CancelablePromise<Tag> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/tags',
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
     * Переместить тег в корзину
     * @returns DeletionReceipt Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static deleteApiV1TagsId({
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
    }): CancelablePromise<DeletionReceipt> {
        return __request(OpenAPI, {
            method: 'DELETE',
            url: '/api/v1/tags/{id}',
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
     * Получить тег
     * @returns Tag Successful response.
     * @throws ApiError
     */
    public static getApiV1TagsId({
        id,
        xCorrelationId,
    }: {
        id: string,
        xCorrelationId?: string,
    }): CancelablePromise<Tag> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/tags/{id}',
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
     * Изменить тег
     * @returns Tag Successful response.
     * @throws ApiError
     */
    public static patchApiV1TagsId({
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
        requestBody: TagPatch,
        xCorrelationId?: string,
    }): CancelablePromise<Tag> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/tags/{id}',
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
     * Восстановить тег
     * @returns Tag Successful response.
     * @throws ApiError
     */
    public static postApiV1TagsIdRestore({
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
    }): CancelablePromise<Tag> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/tags/{id}/restore',
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
}
