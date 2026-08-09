/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ActionResult } from '../models/ActionResult';
import type { Notification } from '../models/Notification';
import type { NotificationActionRequest } from '../models/NotificationActionRequest';
import type { NotificationPage } from '../models/NotificationPage';
import type { NotificationPreferences } from '../models/NotificationPreferences';
import type { NotificationPreferencesPatch } from '../models/NotificationPreferencesPatch';
import type { ReadAllRequest } from '../models/ReadAllRequest';
import type { ReadAllResult } from '../models/ReadAllResult';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class NotificationsService {
    /**
     * Список уведомление
     * @returns NotificationPage Successful response.
     * @throws ApiError
     */
    public static getApiV1Notifications({
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
    }): CancelablePromise<NotificationPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/notifications',
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
     * Настройки уведомлений
     * @returns NotificationPreferences Successful response.
     * @throws ApiError
     */
    public static getApiV1NotificationsPreferences({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<NotificationPreferences> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/notifications/preferences',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
            },
        });
    }
    /**
     * Обновить настройки уведомлений
     * @returns NotificationPreferences Successful response.
     * @throws ApiError
     */
    public static putApiV1NotificationsPreferences({
        idempotencyKey,
        ifMatch,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        requestBody: NotificationPreferencesPatch,
        xCorrelationId?: string,
    }): CancelablePromise<NotificationPreferences> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/notifications/preferences',
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
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
                412: `If-Match does not match the current aggregate version.`,
                422: `Syntactically valid request violates field or domain invariants.`,
                428: `If-Match is required for this operation.`,
            },
        });
    }
    /**
     * Прочитать все в scope
     * @returns ReadAllResult Successful response.
     * @throws ApiError
     */
    public static postApiV1NotificationsReadAll({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: ReadAllRequest,
        xCorrelationId?: string,
    }): CancelablePromise<ReadAllResult> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/notifications/read-all',
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
            },
        });
    }
    /**
     * Получить уведомление
     * @returns Notification Successful response.
     * @throws ApiError
     */
    public static getApiV1NotificationsId({
        id,
        xCorrelationId,
    }: {
        id: string,
        xCorrelationId?: string,
    }): CancelablePromise<Notification> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/notifications/{id}',
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
     * Выполнить разрешённое действие toast
     * @returns ActionResult Successful response.
     * @throws ApiError
     */
    public static postApiV1NotificationsIdAction({
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
        requestBody: NotificationActionRequest,
        xCorrelationId?: string,
    }): CancelablePromise<ActionResult> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/notifications/{id}/action',
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
     * Пометить прочитанным
     * @returns Notification Successful response.
     * @throws ApiError
     */
    public static postApiV1NotificationsIdRead({
        id,
        xCorrelationId,
        ifMatch,
    }: {
        id: string,
        xCorrelationId?: string,
        /**
         * Optional strong ETag used only when a target resource already exists.
         */
        ifMatch?: string,
    }): CancelablePromise<Notification> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/notifications/{id}/read',
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
            },
        });
    }
}
