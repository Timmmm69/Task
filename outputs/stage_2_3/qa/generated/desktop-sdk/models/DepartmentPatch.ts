/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
/**
 * PATCH semantics: omitted properties remain unchanged; an explicit null clears only nullable properties; readOnly properties are rejected; at least one writable property is required.
 */
export type DepartmentPatch = {
    code?: string;
    name?: string;
    description?: string | null;
    parentDepartmentId?: string | null;
    sortOrder?: number;
};

