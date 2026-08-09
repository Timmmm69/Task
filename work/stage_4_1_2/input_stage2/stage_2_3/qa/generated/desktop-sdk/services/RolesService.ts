/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { Permission } from '../models/Permission';
import type { PermissionCodes } from '../models/PermissionCodes';
import type { ProjectRole } from '../models/ProjectRole';
import type { RestoreRequest } from '../models/RestoreRequest';
import type { Role } from '../models/Role';
import type { RoleCreate } from '../models/RoleCreate';
import type { RolePage } from '../models/RolePage';
import type { RolePatch } from '../models/RolePatch';
import type { UserRole } from '../models/UserRole';
import type { UserRolesReplace } from '../models/UserRolesReplace';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class RolesService {
    /**
     * Каталог разрешений
     * @returns Permission Successful response.
     * @throws ApiError
     */
    public static getApiV1Permissions({
        xCorrelationId,
        resource,
    }: {
        xCorrelationId?: string,
        resource?: string,
    }): CancelablePromise<Array<Permission>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/permissions',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'resource': resource,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
            },
        });
    }
    /**
     * Каталог проектных ролей
     * @returns ProjectRole Successful response.
     * @throws ApiError
     */
    public static getApiV1ProjectRoles({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<Array<ProjectRole>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/project-roles',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
            },
        });
    }
    /**
     * Изменить проектную роль
     * @returns ProjectRole Successful response.
     * @throws ApiError
     */
    public static putApiV1ProjectRolesIdPermissions({
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
        requestBody: PermissionCodes,
        xCorrelationId?: string,
    }): CancelablePromise<ProjectRole> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/project-roles/{id}/permissions',
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
                428: `If-Match is required for this operation.`,
            },
        });
    }
    /**
     * Список системную роль
     * @returns RolePage Successful response.
     * @throws ApiError
     */
    public static getApiV1Roles({
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
    }): CancelablePromise<RolePage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/roles',
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
     * Создать системную роль
     * @returns Role Resource created.
     * @throws ApiError
     */
    public static postApiV1Roles({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: RoleCreate,
        xCorrelationId?: string,
    }): CancelablePromise<Role> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/roles',
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
     * Деактивировать пользовательскую роль
     * @returns Role Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static deleteApiV1RolesId({
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
    }): CancelablePromise<Role> {
        return __request(OpenAPI, {
            method: 'DELETE',
            url: '/api/v1/roles/{id}',
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
     * Получить системную роль
     * @returns Role Successful response.
     * @throws ApiError
     */
    public static getApiV1RolesId({
        id,
        xCorrelationId,
    }: {
        id: string,
        xCorrelationId?: string,
    }): CancelablePromise<Role> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/roles/{id}',
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
     * Изменить системную роль
     * @returns Role Successful response.
     * @throws ApiError
     */
    public static patchApiV1RolesId({
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
        requestBody: RolePatch,
        xCorrelationId?: string,
    }): CancelablePromise<Role> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/roles/{id}',
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
     * Активировать неактивную роль
     * @returns Role Successful response.
     * @throws ApiError
     */
    public static postApiV1RolesIdActivate({
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
    }): CancelablePromise<Role> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/roles/{id}/activate',
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
     * Заменить разрешения роли
     * @returns Role Successful response.
     * @throws ApiError
     */
    public static putApiV1RolesIdPermissions({
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
        requestBody: PermissionCodes,
        xCorrelationId?: string,
    }): CancelablePromise<Role> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/roles/{id}/permissions',
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
     * Заменить scoped-роли пользователя
     * @returns UserRole Successful response.
     * @throws ApiError
     */
    public static putApiV1UsersIdRoles({
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
        requestBody: UserRolesReplace,
        xCorrelationId?: string,
    }): CancelablePromise<Array<UserRole>> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/users/{id}/roles',
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
}
