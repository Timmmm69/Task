/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { Checklist } from '../models/Checklist';
import type { ChecklistCreate } from '../models/ChecklistCreate';
import type { ChecklistItem } from '../models/ChecklistItem';
import type { ChecklistItemCreate } from '../models/ChecklistItemCreate';
import type { ChecklistItemPatch } from '../models/ChecklistItemPatch';
import type { ChecklistPage } from '../models/ChecklistPage';
import type { ChecklistPatch } from '../models/ChecklistPatch';
import type { DeletionReceipt } from '../models/DeletionReceipt';
import type { OrderKeys } from '../models/OrderKeys';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class ChecklistsService {
    /**
     * Список чек-лист
     * @returns ChecklistPage Successful response.
     * @throws ApiError
     */
    public static getApiV1TasksTaskIdChecklists({
        taskId,
        xCorrelationId,
        filter,
        sort,
        page,
        cursor,
    }: {
        taskId: string,
        xCorrelationId?: string,
        filter?: string,
        sort?: string,
        page?: number,
        /**
         * Opaque cursor bound to normalized filters, stable sort, authorization scope version and search-index snapshot. Reusing it with different filters is invalid.
         */
        cursor?: string,
    }): CancelablePromise<ChecklistPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/tasks/{taskId}/checklists',
            path: {
                'taskId': taskId,
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
     * Создать чек-лист
     * @returns Checklist Resource created.
     * @throws ApiError
     */
    public static postApiV1TasksTaskIdChecklists({
        taskId,
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        taskId: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: ChecklistCreate,
        xCorrelationId?: string,
    }): CancelablePromise<Checklist> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/tasks/{taskId}/checklists',
            path: {
                'taskId': taskId,
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
     * Добавить пункт чек-листа
     * @returns ChecklistItem Resource created.
     * @throws ApiError
     */
    public static postApiV1TasksTaskIdChecklistsChecklistIdItems({
        taskId,
        checklistId,
        idempotencyKey,
        ifMatch,
        requestBody,
        xCorrelationId,
    }: {
        taskId: string,
        checklistId: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        requestBody: ChecklistItemCreate,
        xCorrelationId?: string,
    }): CancelablePromise<ChecklistItem> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/tasks/{taskId}/checklists/{checklistId}/items',
            path: {
                'taskId': taskId,
                'checklistId': checklistId,
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
     * Удалить пункт
     * @returns void
     * @throws ApiError
     */
    public static deleteApiV1TasksTaskIdChecklistsChecklistIdItemsItemId({
        taskId,
        checklistId,
        itemId,
        ifMatch,
        xCorrelationId,
    }: {
        taskId: string,
        checklistId: string,
        itemId: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        xCorrelationId?: string,
    }): CancelablePromise<void> {
        return __request(OpenAPI, {
            method: 'DELETE',
            url: '/api/v1/tasks/{taskId}/checklists/{checklistId}/items/{itemId}',
            path: {
                'taskId': taskId,
                'checklistId': checklistId,
                'itemId': itemId,
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
     * Изменить/выполнить пункт
     * @returns ChecklistItem Successful response.
     * @throws ApiError
     */
    public static patchApiV1TasksTaskIdChecklistsChecklistIdItemsItemId({
        taskId,
        checklistId,
        itemId,
        idempotencyKey,
        ifMatch,
        requestBody,
        xCorrelationId,
    }: {
        taskId: string,
        checklistId: string,
        itemId: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        requestBody: ChecklistItemPatch,
        xCorrelationId?: string,
    }): CancelablePromise<ChecklistItem> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/tasks/{taskId}/checklists/{checklistId}/items/{itemId}',
            path: {
                'taskId': taskId,
                'checklistId': checklistId,
                'itemId': itemId,
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
     * Изменить порядок пунктов
     * @returns Checklist Successful response.
     * @throws ApiError
     */
    public static postApiV1TasksTaskIdChecklistsChecklistIdReorder({
        taskId,
        checklistId,
        idempotencyKey,
        ifMatch,
        requestBody,
        xCorrelationId,
    }: {
        taskId: string,
        checklistId: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        requestBody: OrderKeys,
        xCorrelationId?: string,
    }): CancelablePromise<Checklist> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/tasks/{taskId}/checklists/{checklistId}/reorder',
            path: {
                'taskId': taskId,
                'checklistId': checklistId,
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
     * Переместить чек-лист в корзину
     * @returns DeletionReceipt Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static deleteApiV1TasksTaskIdChecklistsId({
        taskId,
        id,
        ifMatch,
        xCorrelationId,
    }: {
        taskId: string,
        id: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        xCorrelationId?: string,
    }): CancelablePromise<DeletionReceipt> {
        return __request(OpenAPI, {
            method: 'DELETE',
            url: '/api/v1/tasks/{taskId}/checklists/{id}',
            path: {
                'taskId': taskId,
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
     * Получить чек-лист
     * @returns Checklist Successful response.
     * @throws ApiError
     */
    public static getApiV1TasksTaskIdChecklistsId({
        taskId,
        id,
        xCorrelationId,
    }: {
        taskId: string,
        id: string,
        xCorrelationId?: string,
    }): CancelablePromise<Checklist> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/tasks/{taskId}/checklists/{id}',
            path: {
                'taskId': taskId,
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
     * Изменить чек-лист
     * @returns Checklist Successful response.
     * @throws ApiError
     */
    public static patchApiV1TasksTaskIdChecklistsId({
        taskId,
        id,
        ifMatch,
        requestBody,
        xCorrelationId,
    }: {
        taskId: string,
        id: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        requestBody: ChecklistPatch,
        xCorrelationId?: string,
    }): CancelablePromise<Checklist> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/tasks/{taskId}/checklists/{id}',
            path: {
                'taskId': taskId,
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
