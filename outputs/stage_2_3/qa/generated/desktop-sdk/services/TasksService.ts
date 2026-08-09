/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ArchiveRequest } from '../models/ArchiveRequest';
import type { BulkOperationResult } from '../models/BulkOperationResult';
import type { BulkTaskTransition } from '../models/BulkTaskTransition';
import type { DeletionReceipt } from '../models/DeletionReceipt';
import type { HistoryEntryPage } from '../models/HistoryEntryPage';
import type { RestoreRequest } from '../models/RestoreRequest';
import type { Task } from '../models/Task';
import type { TaskAssigneesReplace } from '../models/TaskAssigneesReplace';
import type { TaskCreate } from '../models/TaskCreate';
import type { TaskDependency } from '../models/TaskDependency';
import type { TaskDependencyCreate } from '../models/TaskDependencyCreate';
import type { TaskMoveRequest } from '../models/TaskMoveRequest';
import type { TaskPage } from '../models/TaskPage';
import type { TaskPatch } from '../models/TaskPatch';
import type { TaskTransitionRequest } from '../models/TaskTransitionRequest';
import type { TaskWatchersReplace } from '../models/TaskWatchersReplace';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class TasksService {
    /**
     * Список задачу
     * @returns TaskPage Successful response.
     * @throws ApiError
     */
    public static getApiV1Tasks({
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
    }): CancelablePromise<TaskPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/tasks',
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
     * Создать задачу
     * @returns Task Resource created.
     * @throws ApiError
     */
    public static postApiV1Tasks({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: TaskCreate,
        xCorrelationId?: string,
    }): CancelablePromise<Task> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/tasks',
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
     * Пакетная смена статуса
     * @returns BulkOperationResult Successful response.
     * @throws ApiError
     */
    public static postApiV1TasksBulkTransition({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: BulkTaskTransition,
        xCorrelationId?: string,
    }): CancelablePromise<BulkOperationResult> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/tasks/bulk-transition',
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
                412: `If-Match does not match the current aggregate version.`,
                422: `Syntactically valid request violates field or domain invariants.`,
                428: `If-Match is required for this operation.`,
            },
        });
    }
    /**
     * Переместить задачу в корзину
     * @returns DeletionReceipt Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static deleteApiV1TasksId({
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
            url: '/api/v1/tasks/{id}',
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
     * Получить задачу
     * @returns Task Successful response.
     * @throws ApiError
     */
    public static getApiV1TasksId({
        id,
        xCorrelationId,
    }: {
        id: string,
        xCorrelationId?: string,
    }): CancelablePromise<Task> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/tasks/{id}',
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
     * Изменить задачу
     * @returns Task Successful response.
     * @throws ApiError
     */
    public static patchApiV1TasksId({
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
        requestBody: TaskPatch,
        xCorrelationId?: string,
    }): CancelablePromise<Task> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/tasks/{id}',
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
     * Архивировать задачу
     * @returns Task Successful response.
     * @throws ApiError
     */
    public static postApiV1TasksIdArchive({
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
    }): CancelablePromise<Task> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/tasks/{id}/archive',
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
     * Заменить исполнителей
     * @returns Task Successful response.
     * @throws ApiError
     */
    public static putApiV1TasksIdAssignees({
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
        requestBody: TaskAssigneesReplace,
        xCorrelationId?: string,
    }): CancelablePromise<Task> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/tasks/{id}/assignees',
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
     * Добавить зависимость
     * @returns TaskDependency Resource created.
     * @throws ApiError
     */
    public static postApiV1TasksIdDependencies({
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
        requestBody: TaskDependencyCreate,
        xCorrelationId?: string,
    }): CancelablePromise<TaskDependency> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/tasks/{id}/dependencies',
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
     * Удалить зависимость
     * @returns void
     * @throws ApiError
     */
    public static deleteApiV1TasksIdDependenciesDependencyId({
        id,
        dependencyId,
        ifMatch,
        xCorrelationId,
    }: {
        id: string,
        dependencyId: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        xCorrelationId?: string,
    }): CancelablePromise<void> {
        return __request(OpenAPI, {
            method: 'DELETE',
            url: '/api/v1/tasks/{id}/dependencies/{dependencyId}',
            path: {
                'id': id,
                'dependencyId': dependencyId,
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
     * История задачи
     * @returns HistoryEntryPage Successful response.
     * @throws ApiError
     */
    public static getApiV1TasksIdHistory({
        id,
        xCorrelationId,
        cursor,
        page,
    }: {
        id: string,
        xCorrelationId?: string,
        /**
         * Opaque cursor bound to normalized filters, stable sort, authorization scope version and search-index snapshot. Reusing it with different filters is invalid.
         */
        cursor?: string,
        page?: number,
    }): CancelablePromise<HistoryEntryPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/tasks/{id}/history',
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
     * Изменить дату/время/длительность
     * @returns Task Successful response.
     * @throws ApiError
     */
    public static postApiV1TasksIdMove({
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
        requestBody: TaskMoveRequest,
        xCorrelationId?: string,
    }): CancelablePromise<Task> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/tasks/{id}/move',
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
     * Восстановить задачу
     * @returns Task Successful response.
     * @throws ApiError
     */
    public static postApiV1TasksIdRestore({
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
    }): CancelablePromise<Task> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/tasks/{id}/restore',
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
     * Список подзадач
     * @returns Task Successful response.
     * @throws ApiError
     */
    public static getApiV1TasksIdSubtasks({
        id,
        xCorrelationId,
        sort,
    }: {
        id: string,
        xCorrelationId?: string,
        sort?: string,
    }): CancelablePromise<Array<Task>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/tasks/{id}/subtasks',
            path: {
                'id': id,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'sort': sort,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                404: `Resource is absent or hidden by authorization scope.`,
            },
        });
    }
    /**
     * Создать подзадачу
     * @returns Task Resource created.
     * @throws ApiError
     */
    public static postApiV1TasksIdSubtasks({
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
        requestBody: TaskCreate,
        xCorrelationId?: string,
    }): CancelablePromise<Task> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/tasks/{id}/subtasks',
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
     * Изменить статус задачи
     * @returns Task Successful response.
     * @throws ApiError
     */
    public static postApiV1TasksIdTransition({
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
        requestBody: TaskTransitionRequest,
        xCorrelationId?: string,
    }): CancelablePromise<Task> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/tasks/{id}/transition',
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
     * Вернуть задачу из архива
     * @returns Task Successful response.
     * @throws ApiError
     */
    public static postApiV1TasksIdUnarchive({
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
    }): CancelablePromise<Task> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/tasks/{id}/unarchive',
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
     * Заменить наблюдателей
     * @returns Task Successful response.
     * @throws ApiError
     */
    public static putApiV1TasksIdWatchers({
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
        requestBody: TaskWatchersReplace,
        xCorrelationId?: string,
    }): CancelablePromise<Task> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/tasks/{id}/watchers',
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
}
