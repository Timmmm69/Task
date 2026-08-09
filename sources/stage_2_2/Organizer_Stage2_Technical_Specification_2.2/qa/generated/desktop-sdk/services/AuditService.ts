/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { AuditEntryPage } from '../models/AuditEntryPage';
import type { ExportJob } from '../models/ExportJob';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class AuditService {
    /**
     * Технический аудит
     * @returns AuditEntryPage Successful response.
     * @throws ApiError
     */
    public static getApiV1Audit({
        xCorrelationId,
        actor,
        action,
        object,
        outcome,
        from,
        to,
        cursor,
    }: {
        xCorrelationId?: string,
        actor?: string,
        action?: string,
        object?: string,
        outcome?: 'success' | 'denied' | 'failure',
        from?: string,
        to?: string,
        cursor?: string,
    }): CancelablePromise<AuditEntryPage> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/audit',
            headers: {
                'X-Correlation-ID': xCorrelationId,
            },
            query: {
                'actor': actor,
                'action': action,
                'object': object,
                'outcome': outcome,
                'from': from,
                'to': to,
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
     * Экспорт ограниченного журнала
     * @returns ExportJob Command accepted for asynchronous execution.
     * @throws ApiError
     */
    public static getApiV1AuditExport({
        idempotencyKey,
        xCorrelationId,
        filters,
        format,
    }: {
        /**
         * Opaque 8-200 character key. Stored with the SHA-256 request hash.
         */
        idempotencyKey: string,
        xCorrelationId?: string,
        filters?: string,
        format?: 'json' | 'csv',
    }): CancelablePromise<ExportJob> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/v1/audit/export',
            headers: {
                'X-Correlation-ID': xCorrelationId,
                'Idempotency-Key': idempotencyKey,
            },
            query: {
                'filters': filters,
                'format': format,
            },
            errors: {
                400: `Malformed request or invalid query syntax.`,
                401: `Authentication failed or token is no longer valid.`,
                403: `Authenticated principal lacks the required permission or relationship.`,
                429: `Rate limit exceeded.`,
            },
        });
    }
}
