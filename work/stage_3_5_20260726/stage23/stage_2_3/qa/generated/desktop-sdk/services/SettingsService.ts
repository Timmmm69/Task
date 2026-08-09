/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { FeatureFlags } from '../models/FeatureFlags';
import type { NotificationUrgencyScale } from '../models/NotificationUrgencyScale';
import type { NotificationUrgencyScalePatch } from '../models/NotificationUrgencyScalePatch';
import type { OrganizationSettings } from '../models/OrganizationSettings';
import type { OrganizationSettingsPatch } from '../models/OrganizationSettingsPatch';
import type { ServerCapabilities } from '../models/ServerCapabilities';
import type { UserSettings } from '../models/UserSettings';
import type { UserSettingsPatch } from '../models/UserSettingsPatch';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class SettingsService {
    /**
     * Возможности сервера и min client
     * @returns ServerCapabilities Successful response.
     * @throws ApiError
     */
    public static getApiV1Capabilities({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<ServerCapabilities> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/capabilities',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
            },
        });
    }
    /**
     * Доступные пользователю флаги
     * @returns FeatureFlags Successful response.
     * @throws ApiError
     */
    public static getApiV1FeatureFlags({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<FeatureFlags> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/feature-flags',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
            },
        });
    }
    /**
     * Пользовательские настройки
     * @returns UserSettings Successful response.
     * @throws ApiError
     */
    public static getApiV1SettingsMe({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<UserSettings> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/settings/me',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
            },
        });
    }
    /**
     * Изменить пользовательские настройки
     * @returns UserSettings Successful response.
     * @throws ApiError
     */
    public static patchApiV1SettingsMe({
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
        requestBody: UserSettingsPatch,
        xCorrelationId?: string,
    }): CancelablePromise<UserSettings> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/settings/me',
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
     * Настройки организации
     * @returns OrganizationSettings Successful response.
     * @throws ApiError
     */
    public static getApiV1SettingsOrganization({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<OrganizationSettings> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/settings/organization',
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
     * Изменить настройки организации
     * @returns OrganizationSettings Successful response.
     * @throws ApiError
     */
    public static patchApiV1SettingsOrganization({
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
        requestBody: OrganizationSettingsPatch,
        xCorrelationId?: string,
    }): CancelablePromise<OrganizationSettings> {
        return __request(OpenAPI, {
            method: 'PATCH',
            url: '/api/v1/settings/organization',
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
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
                412: `If-Match does not match the current aggregate version.`,
                422: `Syntactically valid request violates field or domain invariants.`,
                428: `If-Match is required for this operation.`,
            },
        });
    }
    /**
     * Get organization notification urgency scale
     * @returns NotificationUrgencyScale Successful response.
     * @throws ApiError
     */
    public static getApiV1SettingsNotificationUrgencyScale({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<NotificationUrgencyScale> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/settings/notification-urgency-scale',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            errors: {
                401: `Problem response.`,
                403: `Problem response.`,
            },
        });
    }
    /**
     * Replace organization notification urgency scale
     * @returns NotificationUrgencyScale Successful response.
     * @throws ApiError
     */
    public static putApiV1SettingsNotificationUrgencyScale({
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
        requestBody: NotificationUrgencyScalePatch,
        xCorrelationId?: string,
    }): CancelablePromise<NotificationUrgencyScale> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/settings/notification-urgency-scale',
            headers: {
                'X-Correlation-ID': xCorrelationId,
                'Idempotency-Key': idempotencyKey,
                'If-Match': ifMatch,
            },
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Problem response.`,
                401: `Problem response.`,
                403: `Problem response.`,
                409: `Problem response.`,
                412: `Problem response.`,
                422: `Problem response.`,
                428: `Problem response.`,
            },
        });
    }
    /**
     * Reset organization urgency scale to defaults
     * @returns NotificationUrgencyScale Successful response.
     * @throws ApiError
     */
    public static postApiV1SettingsNotificationUrgencyScaleReset({
        idempotencyKey,
        ifMatch,
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
        xCorrelationId?: string,
    }): CancelablePromise<NotificationUrgencyScale> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/settings/notification-urgency-scale/reset',
            headers: {
                'X-Correlation-ID': xCorrelationId,
                'Idempotency-Key': idempotencyKey,
                'If-Match': ifMatch,
            },
            errors: {
                401: `Problem response.`,
                403: `Problem response.`,
                409: `Problem response.`,
                412: `Problem response.`,
                428: `Problem response.`,
            },
        });
    }
}
