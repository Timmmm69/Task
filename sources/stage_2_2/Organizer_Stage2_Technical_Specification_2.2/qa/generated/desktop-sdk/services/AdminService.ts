/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { BackgroundJobPage } from '../models/BackgroundJobPage';
import type { BackgroundJobRun } from '../models/BackgroundJobRun';
import type { BackgroundJobRunPage } from '../models/BackgroundJobRunPage';
import type { BackupRequest } from '../models/BackupRequest';
import type { BackupRun } from '../models/BackupRun';
import type { BackupRunPage } from '../models/BackupRunPage';
import type { BackupVerifyRequest } from '../models/BackupVerifyRequest';
import type { CompactionRequest } from '../models/CompactionRequest';
import type { FeatureFlag } from '../models/FeatureFlag';
import type { FeatureFlagPatch } from '../models/FeatureFlagPatch';
import type { JobRunRequest } from '../models/JobRunRequest';
import type { MaintenanceMode } from '../models/MaintenanceMode';
import type { MaintenanceModeRequest } from '../models/MaintenanceModeRequest';
import type { ReindexRequest } from '../models/ReindexRequest';
import type { RestoreBackupRequest } from '../models/RestoreBackupRequest';
import type { RestorePlan } from '../models/RestorePlan';
import type { ServerCapabilities } from '../models/ServerCapabilities';
import type { StorageStatus } from '../models/StorageStatus';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class AdminService {
    /**
     * Результаты резервного копирования
     * @returns BackupRunPage Successful response.
     * @throws ApiError
     */
    public static getApiV1AdminBackups({
        xCorrelationId,
        type,
        status,
        from,
        to,
        page,
    }: {
        xCorrelationId?: string,
        type?: string,
        status?: string,
        from?: string,
        to?: string,
        page?: number,
    }): CancelablePromise<BackupRunPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/admin/backups',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'type': type,
                'status': status,
                'from': from,
                'to': to,
                'page': page,
            },
            errors: {
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
            },
        });
    }
    /**
     * Запустить backup
     * @returns BackupRun Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static postApiV1AdminBackups({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: BackupRequest,
        xCorrelationId?: string,
    }): CancelablePromise<BackupRun> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/admin/backups',
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
                429: `Rate limit exceeded.`,
            },
        });
    }
    /**
     * Запросить controlled restore
     * @returns RestorePlan Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static postApiV1AdminBackupsIdRestore({
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
        requestBody: RestoreBackupRequest,
        xCorrelationId?: string,
    }): CancelablePromise<RestorePlan> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/admin/backups/{id}/restore',
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
     * Запустить restore verification
     * @returns BackupRun Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static postApiV1AdminBackupsIdVerify({
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
        requestBody: BackupVerifyRequest,
        xCorrelationId?: string,
    }): CancelablePromise<BackupRun> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/admin/backups/{id}/verify',
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
            },
        });
    }
    /**
     * Запустить compaction change feed
     * @returns BackgroundJobRun Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static postApiV1AdminChangeFeedCompact({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: CompactionRequest,
        xCorrelationId?: string,
    }): CancelablePromise<BackgroundJobRun> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/admin/change-feed/compact',
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
            },
        });
    }
    /**
     * Изменить feature flag
     * @returns FeatureFlag Successful response.
     * @throws ApiError
     */
    public static putApiV1AdminFeatureFlagsKey({
        key,
        idempotencyKey,
        ifMatch,
        requestBody,
        xCorrelationId,
    }: {
        key: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        /**
         * Strong ETag in the form "v<positive-int64>".
         */
        ifMatch: string,
        requestBody: FeatureFlagPatch,
        xCorrelationId?: string,
    }): CancelablePromise<FeatureFlag> {
        return __request(OpenAPI, {
            method: 'PUT',
            url: '/api/v1/admin/feature-flags/{key}',
            path: {
                'key': key,
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
     * Фоновые задания
     * @returns BackgroundJobPage Successful response.
     * @throws ApiError
     */
    public static getApiV1AdminJobs({
        xCorrelationId,
        status,
        page,
    }: {
        xCorrelationId?: string,
        status?: string,
        page?: number,
    }): CancelablePromise<BackgroundJobPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/admin/jobs',
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
     * Запустить разрешённый job
     * @returns BackgroundJobRun Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static postApiV1AdminJobsCodeRun({
        code,
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        code: string,
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: JobRunRequest,
        xCorrelationId?: string,
    }): CancelablePromise<BackgroundJobRun> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/admin/jobs/{code}/run',
            path: {
                'code': code,
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
                429: `Rate limit exceeded.`,
            },
        });
    }
    /**
     * История job
     * @returns BackgroundJobRunPage Successful response.
     * @throws ApiError
     */
    public static getApiV1AdminJobsCodeRuns({
        code,
        xCorrelationId,
        from,
        to,
        status,
        page,
    }: {
        code: string,
        xCorrelationId?: string,
        from?: string,
        to?: string,
        status?: string,
        page?: number,
    }): CancelablePromise<BackgroundJobRunPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/admin/jobs/{code}/runs',
            path: {
                'code': code,
            },
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'from': from,
                'to': to,
                'status': status,
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
     * Включить/выключить maintenance mode
     * @returns MaintenanceMode Successful response.
     * @throws ApiError
     */
    public static postApiV1AdminMaintenanceMode({
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
        requestBody: MaintenanceModeRequest,
        xCorrelationId?: string,
    }): CancelablePromise<MaintenanceMode> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/admin/maintenance-mode',
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
                428: `If-Match is required for this operation.`,
            },
        });
    }
    /**
     * Запустить переиндексацию
     * @returns BackgroundJobRun Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static postApiV1AdminSearchReindex({
        idempotencyKey,
        requestBody,
        xCorrelationId,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        requestBody: ReindexRequest,
        xCorrelationId?: string,
    }): CancelablePromise<BackgroundJobRun> {
        return __request(OpenAPI, {
            method: 'POST',
            url: '/api/v1/admin/search/reindex',
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
            },
        });
    }
    /**
     * Административные capabilities
     * @returns ServerCapabilities Successful response.
     * @throws ApiError
     */
    public static getApiV1AdminServerCapabilities({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<ServerCapabilities> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/admin/server-capabilities',
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
     * Использование диска БД/backup/log
     * @returns StorageStatus Successful response.
     * @throws ApiError
     */
    public static getApiV1AdminStorage({
        xCorrelationId,
    }: {
        xCorrelationId?: string,
    }): CancelablePromise<StorageStatus> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/admin/storage',
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
}
