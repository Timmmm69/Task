/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ActivationRequest } from '../models/ActivationRequest';
import type { BlockUserRequest } from '../models/BlockUserRequest';
import type { DeactivateUserRequest } from '../models/DeactivateUserRequest';
import type { EffectivePermissions } from '../models/EffectivePermissions';
import type { HistoryEntryPage } from '../models/HistoryEntryPage';
import type { User } from '../models/User';
import type { UserCreate } from '../models/UserCreate';
import type { UserPage } from '../models/UserPage';
import type { UserPatch } from '../models/UserPatch';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class UsersService {
    /**
     * Список пользователя
     * @returns UserPage Successful response.
     * @throws ApiError
     */
    public static getApiV1Users({
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
        cursor?: string,
    }): CancelablePromise<UserPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/users',
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
     * Создать пользователя
     * @returns User Resource created.
     * @throws ApiError
     */
    public static postApiV1Users({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: UserCreate,
        xCorrelationId?: string,
    }): CancelablePromise<User> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/users',
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
     * Получить пользователя
     * @returns User Successful response.
     * @throws ApiError
     */
    public static getApiV1UsersId({
        id,
        xCorrelationId,
    }: {
        id: string,
        xCorrelationId?: string,
    }): CancelablePromise<User> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/users/{id}',
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
     * Изменить пользователя
     * @returns User Successful response.
     * @throws ApiError
     */
    public static patchApiV1UsersId({
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
        requestBody: UserPatch,
        xCorrelationId?: string,
    }): CancelablePromise<User> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/users/{id}',
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
     * Активировать учётную запись
     * @returns User Successful response.
     * @throws ApiError
     */
    public static postApiV1UsersIdActivate({
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
        requestBody: ActivationRequest,
        xCorrelationId?: string,
    }): CancelablePromise<User> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/users/{id}/activate',
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
     * Заблокировать пользователя
     * @returns User Successful response.
     * @throws ApiError
     */
    public static postApiV1UsersIdBlock({
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
        requestBody: BlockUserRequest,
        xCorrelationId?: string,
    }): CancelablePromise<User> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/users/{id}/block',
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
                428: `If-Match is required for this operation.`,
            },
        });
    }
    /**
     * Деактивировать учётную запись без удаления профиля
     * @returns User Successful response.
     * @throws ApiError
     */
    public static postApiV1UsersIdDeactivate({
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
        requestBody: DeactivateUserRequest,
        xCorrelationId?: string,
    }): CancelablePromise<User> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/users/{id}/deactivate',
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
                428: `If-Match is required for this operation.`,
            },
        });
    }
    /**
     * Диагностика эффективных прав
     * @returns EffectivePermissions Successful response.
     * @throws ApiError
     */
    public static getApiV1UsersIdEffectivePermissions({
        id,
        xCorrelationId,
        objectType,
        objectId,
    }: {
        id: string,
        xCorrelationId?: string,
        objectType?: string,
        objectId?: string,
    }): CancelablePromise<EffectivePermissions> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/users/{id}/effective-permissions',
            path: {
                'id': id,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'objectType': objectType,
                'objectId': objectId,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                404: `Resource is absent or hidden by authorization scope.`,
            },
        });
    }
    /**
     * История пользователя
     * @returns HistoryEntryPage Successful response.
     * @throws ApiError
     */
    public static getApiV1UsersIdHistory({
        id,
        xCorrelationId,
        cursor,
        page,
    }: {
        id: string,
        xCorrelationId?: string,
        cursor?: string,
        page?: number,
    }): CancelablePromise<HistoryEntryPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/users/{id}/history',
            path: {
                'id': id,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'cursor': cursor,
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
     * Повторно активировать деактивированную учётную запись
     * @returns User Successful response.
     * @throws ApiError
     */
    public static postApiV1UsersIdReactivate({
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
    }): CancelablePromise<User> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/users/{id}/reactivate',
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
     * Разблокировать пользователя
     * @returns User Successful response.
     * @throws ApiError
     */
    public static postApiV1UsersIdUnblock({
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
    }): CancelablePromise<User> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/users/{id}/unblock',
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
}
