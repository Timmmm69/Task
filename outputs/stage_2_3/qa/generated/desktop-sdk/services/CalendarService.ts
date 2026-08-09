/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ArchiveRequest } from '../models/ArchiveRequest';
import type { AttendeeResponse } from '../models/AttendeeResponse';
import type { AttendeesReplace } from '../models/AttendeesReplace';
import type { CalendarEvent } from '../models/CalendarEvent';
import type { CalendarEventCreate } from '../models/CalendarEventCreate';
import type { CalendarEventPage } from '../models/CalendarEventPage';
import type { CalendarEventPatch } from '../models/CalendarEventPatch';
import type { DeletionReceipt } from '../models/DeletionReceipt';
import type { EventAttendee } from '../models/EventAttendee';
import type { RestoreRequest } from '../models/RestoreRequest';
import type { ScheduleConflict } from '../models/ScheduleConflict';
import type { SchedulePage } from '../models/SchedulePage';
import type { TodayPage } from '../models/TodayPage';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class CalendarService {
    /**
     * Объединённая проекция Task + Event
     * @returns SchedulePage Successful response.
     * @throws ApiError
     */
    public static getApiV1Calendar({
        xCorrelationId,
        from,
        to,
        users,
        departments,
        projects,
        status,
        timezone,
        cursor,
    }: {
        xCorrelationId?: string,
        from?: string,
        to?: string,
        users?: Array<string>,
        departments?: Array<string>,
        projects?: Array<string>,
        status?: string,
        timezone?: string,
        /**
         * Opaque cursor bound to normalized filters, stable sort, authorization scope version and search-index snapshot. Reusing it with different filters is invalid.
         */
        cursor?: string,
    }): CancelablePromise<SchedulePage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/calendar',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'from': from,
                'to': to,
                'users': users,
                'departments': departments,
                'projects': projects,
                'status': status,
                'timezone': timezone,
                'cursor': cursor,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                422: `Syntactically valid request violates field or domain invariants.`,
            },
        });
    }
    /**
     * Список календарное событие
     * @returns CalendarEventPage Successful response.
     * @throws ApiError
     */
    public static getApiV1CalendarEvents({
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
    }): CancelablePromise<CalendarEventPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/calendar-events',
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
     * Создать календарное событие
     * @returns CalendarEvent Resource created.
     * @throws ApiError
     */
    public static postApiV1CalendarEvents({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: CalendarEventCreate,
        xCorrelationId?: string,
    }): CancelablePromise<CalendarEvent> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/calendar-events',
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
     * Переместить календарное событие в корзину
     * @returns DeletionReceipt Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static deleteApiV1CalendarEventsId({
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
            url: '/api/v1/calendar-events/{id}',
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
     * Получить календарное событие
     * @returns CalendarEvent Successful response.
     * @throws ApiError
     */
    public static getApiV1CalendarEventsId({
        id,
        xCorrelationId,
    }: {
        id: string,
        xCorrelationId?: string,
    }): CancelablePromise<CalendarEvent> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/calendar-events/{id}',
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
     * Изменить календарное событие
     * @returns CalendarEvent Successful response.
     * @throws ApiError
     */
    public static patchApiV1CalendarEventsId({
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
        requestBody: CalendarEventPatch,
        xCorrelationId?: string,
    }): CancelablePromise<CalendarEvent> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/calendar-events/{id}',
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
     * Архивировать календарное событие
     * @returns CalendarEvent Successful response.
     * @throws ApiError
     */
    public static postApiV1CalendarEventsIdArchive({
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
        requestBody: ArchiveRequest,
        xCorrelationId?: string,
    }): CancelablePromise<CalendarEvent> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/calendar-events/{id}/archive',
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
     * Заменить участников события
     * @returns CalendarEvent Successful response.
     * @throws ApiError
     */
    public static putApiV1CalendarEventsIdAttendees({
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
        requestBody: AttendeesReplace,
        xCorrelationId?: string,
    }): CancelablePromise<CalendarEvent> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/calendar-events/{id}/attendees',
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
     * Ответить на приглашение
     * @returns EventAttendee Successful response.
     * @throws ApiError
     */
    public static postApiV1CalendarEventsIdRespond({
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
        requestBody: AttendeeResponse,
        xCorrelationId?: string,
    }): CancelablePromise<EventAttendee> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/calendar-events/{id}/respond',
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
     * Восстановить календарное событие
     * @returns CalendarEvent Successful response.
     * @throws ApiError
     */
    public static postApiV1CalendarEventsIdRestore({
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
    }): CancelablePromise<CalendarEvent> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/calendar-events/{id}/restore',
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
     * Вернуть календарное событие из архива
     * @returns CalendarEvent Successful response.
     * @throws ApiError
     */
    public static postApiV1CalendarEventsIdUnarchive({
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
    }): CancelablePromise<CalendarEvent> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/calendar-events/{id}/unarchive',
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
     * Пересечения расписания
     * @returns ScheduleConflict Successful response.
     * @throws ApiError
     */
    public static getApiV1CalendarConflicts({
        xCorrelationId,
        from,
        to,
        userIds,
        excludeObjectId,
    }: {
        xCorrelationId?: string,
        from?: string,
        to?: string,
        userIds?: Array<string>,
        excludeObjectId?: string,
    }): CancelablePromise<Array<ScheduleConflict>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/calendar/conflicts',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'from': from,
                'to': to,
                'userIds': userIds,
                'excludeObjectId': excludeObjectId,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                422: `Syntactically valid request violates field or domain invariants.`,
            },
        });
    }
    /**
     * Агрегированный read-model «Сегодня» в часовом поясе пользователя
     * @returns TodayPage Successful response.
     * @throws ApiError
     */
    public static getApiV1Today({
        xCorrelationId,
        timezone,
        cursor,
        limit,
    }: {
        xCorrelationId?: string,
        timezone?: string,
        /**
         * Opaque cursor bound to normalized filters, stable sort, authorization scope version and search-index snapshot. Reusing it with different filters is invalid.
         */
        cursor?: string,
        /**
         * Maximum number of authorization-filtered results returned by the server.
         */
        limit?: number,
    }): CancelablePromise<TodayPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/today',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'timezone': timezone,
                'cursor': cursor,
                'limit': limit,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                422: `Syntactically valid request violates field or domain invariants.`,
            },
        });
    }
}
