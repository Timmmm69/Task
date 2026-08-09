/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { DismissReminderRequest } from '../models/DismissReminderRequest';
import type { Reminder } from '../models/Reminder';
import type { ReminderCreate } from '../models/ReminderCreate';
import type { ReminderOccurrence } from '../models/ReminderOccurrence';
import type { ReminderPage } from '../models/ReminderPage';
import type { ReminderPatch } from '../models/ReminderPatch';
import type { RestoreRequest } from '../models/RestoreRequest';
import type { SnoozeRequest } from '../models/SnoozeRequest';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class RemindersService {
    /**
     * Список напоминание
     * @returns ReminderPage Successful response.
     * @throws ApiError
     */
    public static getApiV1Reminders({
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
    }): CancelablePromise<ReminderPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/reminders',
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
     * Создать напоминание
     * @returns Reminder Resource created.
     * @throws ApiError
     */
    public static postApiV1Reminders({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: ReminderCreate,
        xCorrelationId?: string,
    }): CancelablePromise<Reminder> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/reminders',
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
     * Ближайшие напоминания
     * @returns ReminderOccurrence Successful response.
     * @throws ApiError
     */
    public static getApiV1RemindersUpcoming({
        xCorrelationId,
        until,
        limit,
    }: {
        xCorrelationId?: string,
        until?: string,
        limit?: number,
    }): CancelablePromise<Array<ReminderOccurrence>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/reminders/upcoming',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'until': until,
                'limit': limit,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
            },
        });
    }
    /**
     * Отменить напоминание
     * @returns Reminder Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static deleteApiV1RemindersId({
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
    }): CancelablePromise<Reminder> {
        return __request(OpenAPI, {
            method: 'DELETE',
            url: '/api/v1/reminders/{id}',
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
     * Получить напоминание
     * @returns Reminder Successful response.
     * @throws ApiError
     */
    public static getApiV1RemindersId({
        id,
        xCorrelationId,
    }: {
        id: string,
        xCorrelationId?: string,
    }): CancelablePromise<Reminder> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/reminders/{id}',
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
     * Изменить напоминание
     * @returns Reminder Successful response.
     * @throws ApiError
     */
    public static patchApiV1RemindersId({
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
        requestBody: ReminderPatch,
        xCorrelationId?: string,
    }): CancelablePromise<Reminder> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/reminders/{id}',
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
     * Закрыть срабатывание
     * @returns void
     * @throws ApiError
     */
    public static postApiV1RemindersIdDismiss({
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
        requestBody: DismissReminderRequest,
        xCorrelationId?: string,
    }): CancelablePromise<void> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/reminders/{id}/dismiss',
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
     * Перепланировать отменённое напоминание
     * @returns Reminder Successful response.
     * @throws ApiError
     */
    public static postApiV1RemindersIdReschedule({
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
    }): CancelablePromise<Reminder> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/reminders/{id}/reschedule',
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
     * Отложить напоминание
     * @returns ReminderOccurrence Successful response.
     * @throws ApiError
     */
    public static postApiV1RemindersIdSnooze({
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
        requestBody: SnoozeRequest,
        xCorrelationId?: string,
    }): CancelablePromise<ReminderOccurrence> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/reminders/{id}/snooze',
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
