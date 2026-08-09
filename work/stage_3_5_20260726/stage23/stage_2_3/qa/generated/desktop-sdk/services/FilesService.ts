/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ArchiveRequest } from '../models/ArchiveRequest';
import type { CatalogItem } from '../models/CatalogItem';
import type { CatalogItemCreate } from '../models/CatalogItemCreate';
import type { CatalogItemPage } from '../models/CatalogItemPage';
import type { CatalogItemPatch } from '../models/CatalogItemPatch';
import type { CatalogMoveRequest } from '../models/CatalogMoveRequest';
import type { CatalogTree } from '../models/CatalogTree';
import type { DeletionReceipt } from '../models/DeletionReceipt';
import type { FileLocation } from '../models/FileLocation';
import type { FileLocationCheckCreate } from '../models/FileLocationCheckCreate';
import type { FileLocationCreate } from '../models/FileLocationCreate';
import type { FileLocationPatch } from '../models/FileLocationPatch';
import type { NetworkResource } from '../models/NetworkResource';
import type { NetworkResourceCreate } from '../models/NetworkResourceCreate';
import type { NetworkResourcePage } from '../models/NetworkResourcePage';
import type { NetworkResourcePatch } from '../models/NetworkResourcePatch';
import type { ProbeRequest } from '../models/ProbeRequest';
import type { ProbeResult } from '../models/ProbeResult';
import type { ResolvedLocation } from '../models/ResolvedLocation';
import type { ResolveLocationRequest } from '../models/ResolveLocationRequest';
import type { RestoreRequest } from '../models/RestoreRequest';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class FilesService {
    /**
     * Список элемент каталога
     * @returns CatalogItemPage Successful response.
     * @throws ApiError
     */
    public static getApiV1CatalogItems({
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
    }): CancelablePromise<CatalogItemPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/catalog-items',
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
     * Создать элемент каталога
     * @returns CatalogItem Resource created.
     * @throws ApiError
     */
    public static postApiV1CatalogItems({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: CatalogItemCreate,
        xCorrelationId?: string,
    }): CancelablePromise<CatalogItem> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/catalog-items',
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
     * Переместить элемент каталога в корзину
     * @returns DeletionReceipt Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static deleteApiV1CatalogItemsId({
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
            url: '/api/v1/catalog-items/{id}',
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
     * Получить элемент каталога
     * @returns CatalogItem Successful response.
     * @throws ApiError
     */
    public static getApiV1CatalogItemsId({
        id,
        xCorrelationId,
    }: {
        id: string,
        xCorrelationId?: string,
    }): CancelablePromise<CatalogItem> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/catalog-items/{id}',
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
     * Изменить элемент каталога
     * @returns CatalogItem Successful response.
     * @throws ApiError
     */
    public static patchApiV1CatalogItemsId({
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
        requestBody: CatalogItemPatch,
        xCorrelationId?: string,
    }): CancelablePromise<CatalogItem> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/catalog-items/{id}',
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
     * Архивировать элемент каталога
     * @returns CatalogItem Successful response.
     * @throws ApiError
     */
    public static postApiV1CatalogItemsIdArchive({
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
    }): CancelablePromise<CatalogItem> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/catalog-items/{id}/archive',
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
     * Разрешённые locations
     * @returns FileLocation Successful response.
     * @throws ApiError
     */
    public static getApiV1CatalogItemsIdLocations({
        id,
        xCorrelationId,
        deviceId,
    }: {
        id: string,
        xCorrelationId?: string,
        deviceId?: string,
    }): CancelablePromise<Array<FileLocation>> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/catalog-items/{id}/locations',
            path: {
                'id': id,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'deviceId': deviceId,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                404: `Resource is absent or hidden by authorization scope.`,
            },
        });
    }
    /**
     * Добавить location
     * @returns FileLocation Resource created.
     * @throws ApiError
     */
    public static postApiV1CatalogItemsIdLocations({
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
        requestBody: FileLocationCreate,
        xCorrelationId?: string,
    }): CancelablePromise<FileLocation> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/catalog-items/{id}/locations',
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
     * Удалить location
     * @returns void
     * @throws ApiError
     */
    public static deleteApiV1CatalogItemsIdLocationsLocationId({
        id,
        locationId,
        ifMatch,
        xCorrelationId,
    }: {
        id: string,
        locationId: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        xCorrelationId?: string,
    }): CancelablePromise<void> {
        return __request(OpenAPI, {
            method: 'DELETE',
            url: '/api/v1/catalog-items/{id}/locations/{locationId}',
            path: {
                'id': id,
                'locationId': locationId,
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
    /**
     * Перепривязать location
     * @returns FileLocation Successful response.
     * @throws ApiError
     */
    public static patchApiV1CatalogItemsIdLocationsLocationId({
        id,
        locationId,
        idempotencyKey,
        ifMatch,
        requestBody,
        xCorrelationId,
    }: {
        id: string,
        locationId: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        requestBody: FileLocationPatch,
        xCorrelationId?: string,
    }): CancelablePromise<FileLocation> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/catalog-items/{id}/locations/{locationId}',
            path: {
                'id': id,
                'locationId': locationId,
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
     * Переместить в виртуальном дереве
     * @returns CatalogItem Successful response.
     * @throws ApiError
     */
    public static postApiV1CatalogItemsIdMove({
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
        requestBody: CatalogMoveRequest,
        xCorrelationId?: string,
    }): CancelablePromise<CatalogItem> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/catalog-items/{id}/move',
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
     * Выбрать путь для устройства
     * @returns ResolvedLocation Successful response.
     * @throws ApiError
     */
    public static postApiV1CatalogItemsIdResolveLocation({
        id,
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        id: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: ResolveLocationRequest,
        xCorrelationId?: string,
    }): CancelablePromise<ResolvedLocation> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/catalog-items/{id}/resolve-location',
            path: {
                'id': id,
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
                404: `Resource is absent or hidden by authorization scope.`,
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
                422: `Syntactically valid request violates field or domain invariants.`,
            },
        });
    }
    /**
     * Восстановить элемент каталога
     * @returns CatalogItem Successful response.
     * @throws ApiError
     */
    public static postApiV1CatalogItemsIdRestore({
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
    }): CancelablePromise<CatalogItem> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/catalog-items/{id}/restore',
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
     * Вернуть элемент каталога из архива
     * @returns CatalogItem Successful response.
     * @throws ApiError
     */
    public static postApiV1CatalogItemsIdUnarchive({
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
    }): CancelablePromise<CatalogItem> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/catalog-items/{id}/unarchive',
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
     * Дерево виртуального каталога
     * @returns CatalogTree Successful response.
     * @throws ApiError
     */
    public static getApiV1CatalogTree({
        xCorrelationId,
        parentId,
        depth,
        includeArchived,
    }: {
        xCorrelationId?: string,
        parentId?: string,
        depth?: number,
        includeArchived?: boolean,
    }): CancelablePromise<CatalogTree> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/catalog/tree',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'parentId': parentId,
                'depth': depth,
                'includeArchived': includeArchived,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
            },
        });
    }
    /**
     * Передать результат desktop probe
     * @returns void
     * @throws ApiError
     */
    public static postApiV1FileLocationsIdCheckResult({
        id,
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        id: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: FileLocationCheckCreate,
        xCorrelationId?: string,
    }): CancelablePromise<void> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/file-locations/{id}/check-result',
            path: {
                'id': id,
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
                404: `Resource is absent or hidden by authorization scope.`,
                422: `Syntactically valid request violates field or domain invariants.`,
            },
        });
    }
    /**
     * Сетевые ресурсы
     * @returns NetworkResourcePage Successful response.
     * @throws ApiError
     */
    public static getApiV1NetworkResources({
        xCorrelationId,
        status,
        page,
    }: {
        xCorrelationId?: string,
        status?: string,
        page?: number,
    }): CancelablePromise<NetworkResourcePage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/network-resources',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'status': status,
                'page': page,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
            },
        });
    }
    /**
     * Создать сетевой ресурс
     * @returns NetworkResource Resource created.
     * @throws ApiError
     */
    public static postApiV1NetworkResources({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: NetworkResourceCreate,
        xCorrelationId?: string,
    }): CancelablePromise<NetworkResource> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/network-resources',
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
     * Изменить сетевой ресурс
     * @returns NetworkResource Successful response.
     * @throws ApiError
     */
    public static patchApiV1NetworkResourcesId({
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
        requestBody: NetworkResourcePatch,
        xCorrelationId?: string,
    }): CancelablePromise<NetworkResource> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/network-resources/{id}',
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
     * Проверить ресурс без credentials
     * @returns ProbeResult Successful response.
     * @throws ApiError
     */
    public static postApiV1NetworkResourcesIdProbe({
        id,
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        id: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: ProbeRequest,
        xCorrelationId?: string,
    }): CancelablePromise<ProbeResult> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/network-resources/{id}/probe',
            path: {
                'id': id,
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
                404: `Resource is absent or hidden by authorization scope.`,
                422: `Syntactically valid request violates field or domain invariants.`,
            },
        });
    }
}
