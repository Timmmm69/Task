/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { Health } from '../models/Health';
import type { HealthDetails } from '../models/HealthDetails';
import type { ServerTime } from '../models/ServerTime';
import type { SystemVersion } from '../models/SystemVersion';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class HealthService {
    /**
     * Серверное UTC-время
     * @returns ServerTime Successful response.
     * @throws ApiError
     */
    public static getApiV1SystemTime({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<ServerTime> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/system/time',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
            },
        });
    }
    /**
     * Версия сервера/API/схемы
     * @returns SystemVersion Successful response.
     * @throws ApiError
     */
    public static getApiV1SystemVersion({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<SystemVersion> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/system/version',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
            },
        });
    }
    /**
     * Детальный health
     * @returns HealthDetails Successful response.
     * @throws ApiError
     */
    public static getHealthDetails({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<HealthDetails> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/health/details',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                503: `Required dependency is unavailable.`,
            },
        });
    }
    /**
     * Liveness без зависимостей
     * @returns Health Successful response.
     * @throws ApiError
     */
    public static getHealthLive({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<Health> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/health/live',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                503: `Required dependency is unavailable.`,
            },
        });
    }
    /**
     * Readiness основных зависимостей
     * @returns Health Successful response.
     * @throws ApiError
     */
    public static getHealthReady({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<Health> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/health/ready',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                503: `Required dependency is unavailable.`,
            },
        });
    }
}
