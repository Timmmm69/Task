/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AdminResetPasswordRequest } from '../models/AdminResetPasswordRequest';
import type { ChangePasswordRequest } from '../models/ChangePasswordRequest';
import type { CurrentSession } from '../models/CurrentSession';
import type { LoginAttemptPage } from '../models/LoginAttemptPage';
import type { LoginRequest } from '../models/LoginRequest';
import type { LogoutAllRequest } from '../models/LogoutAllRequest';
import type { RefreshRequest } from '../models/RefreshRequest';
import type { RevocationSummary } from '../models/RevocationSummary';
import type { SessionPage } from '../models/SessionPage';
import type { SessionTokens } from '../models/SessionTokens';
import type { TemporaryCredentialReceipt } from '../models/TemporaryCredentialReceipt';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class AuthService {
    /**
     * Администратор задаёт временный пароль
     * @returns TemporaryCredentialReceipt Successful response.
     * @throws ApiError
     */
    public static postApiV1AuthAdminResetPassword({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: AdminResetPasswordRequest,
        xCorrelationId?: string,
    }): CancelablePromise<TemporaryCredentialReceipt> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/auth/admin-reset-password',
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
            },
        });
    }
    /**
     * Смена собственного пароля
     * @returns void
     * @throws ApiError
     */
    public static postApiV1AuthChangePassword({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: ChangePasswordRequest,
        xCorrelationId?: string,
    }): CancelablePromise<void> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/auth/change-password',
            headers: {
                'X-Correlation-ID': xCorrelationId,
                'Idempotency-Key': idempotencyKey,
            },
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
                422: `Syntactically valid request violates field or domain invariants.`,
            },
        });
    }
    /**
     * Вход по логину/паролю
     * @returns SessionTokens Successful response.
     * @throws ApiError
     */
    public static postApiV1AuthLogin({
        requestBody,
        xCorrelationId,
        idempotencyKey,
    }: {
        requestBody: LoginRequest,
        xCorrelationId?: string,
        /**
         * Optional replay key for naturally single-use or anonymous operations.
         */
        idempotencyKey?: string,
    }): CancelablePromise<SessionTokens> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/auth/login',
            headers: {
                'X-Correlation-ID': xCorrelationId,
                'Idempotency-Key': idempotencyKey,
            },
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                423: `Account or resource is locked.`,
                429: `Rate limit exceeded.`,
            },
        });
    }
    /**
     * Журнал входов
     * @returns LoginAttemptPage Successful response.
     * @throws ApiError
     */
    public static getApiV1AuthLoginAttempts({
        xCorrelationId,
        userId,
        result,
        from,
        to,
        page,
    }: {
        xCorrelationId?: string,
        userId?: string,
        result?: string,
        from?: string,
        to?: string,
        page?: number,
    }): CancelablePromise<LoginAttemptPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/auth/login-attempts',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'userId': userId,
                'result': result,
                'from': from,
                'to': to,
                'page': page,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
            },
        });
    }
    /**
     * Завершить текущую сессию
     * @returns void
     * @throws ApiError
     */
    public static postApiV1AuthLogout({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<void> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/auth/logout',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
            },
        });
    }
    /**
     * Отозвать все сессии пользователя
     * @returns RevocationSummary Successful response.
     * @throws ApiError
     */
    public static postApiV1AuthLogoutAll({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: LogoutAllRequest,
        xCorrelationId?: string,
    }): CancelablePromise<RevocationSummary> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/auth/logout-all',
            headers: {
                'X-Correlation-ID': xCorrelationId,
                'Idempotency-Key': idempotencyKey,
            },
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
            },
        });
    }
    /**
     * Ротация refresh token
     * @returns SessionTokens Successful response.
     * @throws ApiError
     */
    public static postApiV1AuthRefresh({
        requestBody,
        xCorrelationId,
    }: {
        requestBody: RefreshRequest,
        xCorrelationId?: string,
    }): CancelablePromise<SessionTokens> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/auth/refresh',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            body: requestBody,
            mediaType: 'application/json',
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                409: `Domain conflict, idempotency-key collision, or secondary-version conflict.`,
            },
        });
    }
    /**
     * Текущая сессия и capabilities
     * @returns CurrentSession Successful response.
     * @throws ApiError
     */
    public static getApiV1AuthSession({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<CurrentSession> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/auth/session',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
            },
        });
    }
    /**
     * Список собственных/разрешённых сессий
     * @returns SessionPage Successful response.
     * @throws ApiError
     */
    public static getApiV1AuthSessions({
        xCorrelationId,
        userId,
        page,
    }: {
        xCorrelationId?: string,
        userId?: string,
        page?: number,
    }): CancelablePromise<SessionPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/auth/sessions',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'userId': userId,
                'page': page,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
            },
        });
    }
    /**
     * Отозвать сессию
     * @returns void
     * @throws ApiError
     */
    public static postApiV1AuthSessionsSessionIdRevoke({
        sessionId,
        xCorrelationId,
    }: {
        sessionId: string,
        xCorrelationId?: string,
    }): CancelablePromise<void> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/auth/sessions/{sessionId}/revoke',
            path: {
                'sessionId': sessionId,
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
}
