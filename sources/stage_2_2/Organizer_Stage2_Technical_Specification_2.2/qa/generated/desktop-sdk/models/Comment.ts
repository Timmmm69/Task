/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
export type Comment = {
    readonly id: string;
    readonly organizationId: string;
    readonly version: number;
    readonly createdAt?: string;
    readonly updatedAt?: string;
    targetObjectId: string;
    parentCommentId?: string | null;
    readonly authorUserId: string;
    body: string;
    status: 'active' | 'deleted';
    readonly deletedAt?: string | null;
};

