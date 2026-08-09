/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type HealthDetails = {
    status: 'healthy' | 'degraded' | 'unhealthy';
    checks: Array<{
        name: string;
        status: 'healthy' | 'degraded' | 'unhealthy';
        latencyMs: number;
        code?: string | null;
    }>;
    /**
     * RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone.
     */
    checkedAt: string;
};

