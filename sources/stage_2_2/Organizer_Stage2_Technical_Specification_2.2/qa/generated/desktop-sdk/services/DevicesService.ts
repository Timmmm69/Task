/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { Device } from '../models/Device';
import type { DeviceHeartbeat } from '../models/DeviceHeartbeat';
import type { DevicePage } from '../models/DevicePage';
import type { DevicePatch } from '../models/DevicePatch';
import type { RevokeDeviceRequest } from '../models/RevokeDeviceRequest';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class DevicesService {
    /**
     * Список устройство
     * @returns DevicePage Successful response.
     * @throws ApiError
     */
    public static getApiV1Devices({
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
    }): CancelablePromise<DevicePage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/devices',
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
     * Получить устройство
     * @returns Device Successful response.
     * @throws ApiError
     */
    public static getApiV1DevicesId({
        id,
        xCorrelationId,
    }: {
        id: string,
        xCorrelationId?: string,
    }): CancelablePromise<Device> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/devices/{id}',
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
     * Изменить устройство
     * @returns Device Successful response.
     * @throws ApiError
     */
    public static patchApiV1DevicesId({
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
        requestBody: DevicePatch,
        xCorrelationId?: string,
    }): CancelablePromise<Device> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/devices/{id}',
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
     * Обновить last_seen и версию клиента
     * @returns void
     * @throws ApiError
     */
    public static postApiV1DevicesIdHeartbeat({
        id,
        requestBody,
        xCorrelationId,
    }: {
        id: string,
        requestBody: DeviceHeartbeat,
        xCorrelationId?: string,
    }): CancelablePromise<void> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/devices/{id}/heartbeat',
            path: {
                'id': id,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                404: `Resource is absent or hidden by authorization scope.`,
            },
        });
    }
    /**
     * Отозвать устройство
     * @returns Device Successful response.
     * @throws ApiError
     */
    public static postApiV1DevicesIdRevoke({
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
        requestBody: RevokeDeviceRequest,
        xCorrelationId?: string,
    }): CancelablePromise<Device> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/devices/{id}/revoke',
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
}
