from __future__ import annotations

import copy
import csv
import re
from pathlib import Path
from typing import Any

import yaml


ROOT = Path(__file__).resolve().parents[1]
API_CATALOG = ROOT / "catalogs" / "api_catalog.csv"
ERROR_CATALOG = ROOT / "catalogs" / "errors.csv"
OPENAPI = ROOT / "openapi" / "openapi.yaml"


class NoAliasDumper(yaml.SafeDumper):
    def ignore_aliases(self, data: Any) -> bool:
        return True


def allow_null(schema: dict[str, Any]) -> None:
    current_type = schema["type"]
    schema["type"] = [current_type, "null"]


def text(
    *,
    min_length: int | None = None,
    max_length: int | None = None,
    pattern: str | None = None,
    format_name: str | None = None,
    nullable: bool = False,
    read_only: bool = False,
    write_only: bool = False,
) -> dict[str, Any]:
    schema: dict[str, Any] = {"type": "string"}
    if min_length is not None:
        schema["minLength"] = min_length
    if max_length is not None:
        schema["maxLength"] = max_length
    if pattern is not None:
        schema["pattern"] = pattern
    if format_name is not None:
        schema["format"] = format_name
        if format_name == "date-time":
            schema["description"] = (
                "RFC 3339 instant. Serialize in UTC with an explicit Z offset; render in the applicable user or object time zone."
            )
        elif format_name == "date":
            schema["description"] = "Calendar date without a time zone."
        elif format_name == "time":
            schema["description"] = (
                "Local wall-clock time. Interpret only together with the companion IANA time-zone field."
            )
    if nullable:
        allow_null(schema)
    if read_only:
        schema["readOnly"] = True
    if write_only:
        schema["writeOnly"] = True
    return schema


def identifier(*, nullable: bool = False, read_only: bool = False) -> dict[str, Any]:
    return text(format_name="uuid", nullable=nullable, read_only=read_only)


def integer(
    *,
    minimum: int | None = None,
    maximum: int | None = None,
    int_format: str = "int32",
    nullable: bool = False,
    read_only: bool = False,
) -> dict[str, Any]:
    schema: dict[str, Any] = {"type": "integer", "format": int_format}
    if minimum is not None:
        schema["minimum"] = minimum
    if maximum is not None:
        schema["maximum"] = maximum
    if nullable:
        allow_null(schema)
    if read_only:
        schema["readOnly"] = True
    return schema


def boolean(*, nullable: bool = False, read_only: bool = False) -> dict[str, Any]:
    schema: dict[str, Any] = {"type": "boolean"}
    if nullable:
        allow_null(schema)
    if read_only:
        schema["readOnly"] = True
    return schema


def timestamp(*, nullable: bool = False, read_only: bool = False) -> dict[str, Any]:
    return text(format_name="date-time", nullable=nullable, read_only=read_only)


def date(*, nullable: bool = False) -> dict[str, Any]:
    return text(format_name="date", nullable=nullable)


def enum(values: list[str], *, nullable: bool = False) -> dict[str, Any]:
    schema: dict[str, Any] = {"type": "string", "enum": values}
    if nullable:
        allow_null(schema)
        schema["enum"] = [*values, None]
    return schema


def array(
    items: dict[str, Any],
    *,
    min_items: int | None = None,
    max_items: int | None = None,
    unique: bool = False,
) -> dict[str, Any]:
    schema: dict[str, Any] = {"type": "array", "items": items}
    if min_items is not None:
        schema["minItems"] = min_items
    if max_items is not None:
        schema["maxItems"] = max_items
    if unique:
        schema["uniqueItems"] = True
    return schema


def reference(name: str) -> dict[str, str]:
    return {"$ref": f"#/components/schemas/{name}"}


def object_schema(
    properties: dict[str, Any],
    required: list[str] | None = None,
    *,
    min_properties: int | None = None,
    description: str | None = None,
) -> dict[str, Any]:
    schema: dict[str, Any] = {
        "type": "object",
        "additionalProperties": False,
        "properties": properties,
    }
    if required:
        schema["required"] = required
    if min_properties is not None:
        schema["minProperties"] = min_properties
    if description:
        schema["description"] = description
    return schema


def bounded_json_object(description: str) -> dict[str, Any]:
    return {
        "type": "object",
        "description": description,
        "maxProperties": 100,
        "additionalProperties": {
            "oneOf": [
                text(max_length=10000),
                {"type": "number", "format": "double"},
                boolean(),
                {"type": "array", "maxItems": 100, "items": text(max_length=10000)},
            ]
        },
    }


def entity(
    properties: dict[str, Any],
    required: list[str],
) -> dict[str, Any]:
    common = {
        "id": identifier(read_only=True),
        "organizationId": identifier(read_only=True),
        "version": integer(minimum=1, int_format="int64", read_only=True),
        "createdAt": timestamp(read_only=True),
        "updatedAt": timestamp(read_only=True),
    }
    return object_schema({**common, **properties}, ["id", "organizationId", "version", *required])


ENTITIES: dict[str, dict[str, Any]] = {
    "User": entity(
        {
            "displayName": text(min_length=1, max_length=300),
            "firstName": text(min_length=1, max_length=100),
            "lastName": text(min_length=1, max_length=100),
            "login": text(min_length=3, max_length=100),
            "workEmail": text(format_name="email", max_length=320, nullable=True),
            "departmentId": identifier(nullable=True),
            "jobTitle": text(max_length=200, nullable=True),
            "accountStatus": enum(["pending_activation", "active", "blocked", "deactivated"]),
        },
        ["displayName", "firstName", "lastName", "login", "accountStatus"],
    ),
    "Department": entity(
        {
            "code": text(min_length=1, max_length=60),
            "name": text(min_length=1, max_length=200),
            "description": text(max_length=4000, nullable=True),
            "parentDepartmentId": identifier(nullable=True),
            "sortOrder": integer(),
        },
        ["code", "name", "sortOrder"],
    ),
    "Role": entity(
        {
            "code": text(min_length=1, max_length=60),
            "name": text(min_length=1, max_length=200),
            "scopeType": enum(["organization", "department"]),
            "isSystem": boolean(read_only=True),
            "status": enum(["active", "inactive"]),
            "permissionCodes": array(text(max_length=100), max_items=200, unique=True),
        },
        ["code", "name", "scopeType", "isSystem", "status", "permissionCodes"],
    ),
    "ProjectRole": entity(
        {
            "code": text(min_length=1, max_length=60),
            "name": text(min_length=1, max_length=200),
            "isSystem": boolean(read_only=True),
            "status": enum(["active", "inactive"]),
            "permissionCodes": array(text(max_length=100), max_items=100, unique=True),
        },
        ["code", "name", "isSystem", "status", "permissionCodes"],
    ),
    "Device": entity(
        {
            "deviceKey": text(min_length=16, max_length=200, write_only=True),
            "deviceName": text(min_length=1, max_length=200),
            "platform": enum(["windows", "linux", "macos"]),
            "appVersion": text(min_length=1, max_length=32),
            "status": enum(["active", "revoked"]),
            "lastSeenAt": timestamp(nullable=True, read_only=True),
        },
        ["deviceKey", "deviceName", "platform", "appVersion", "status"],
    ),
    "Project": entity(
        {
            "name": text(min_length=1, max_length=300),
            "description": text(max_length=20000, nullable=True),
            "ownerUserId": identifier(),
            "managerUserId": identifier(nullable=True),
            "status": enum(["planning", "active", "paused", "completed"]),
            "startDate": date(nullable=True),
            "plannedEndDate": date(nullable=True),
            "actualEndAt": timestamp(nullable=True),
            "defaultTimeZone": text(max_length=64, nullable=True),
            "colorCode": text(max_length=9, pattern=r"^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$", nullable=True),
        },
        ["name", "ownerUserId", "status"],
    ),
    "ProjectMember": entity(
        {
            "projectId": identifier(),
            "userAccountId": identifier(),
            "projectRoleId": identifier(),
            "status": enum(["invited", "active", "removed"]),
            "joinedAt": timestamp(nullable=True),
            "removedAt": timestamp(nullable=True),
        },
        ["projectId", "userAccountId", "projectRoleId", "status"],
    ),
    "InboxItem": entity(
        {
            "ownerUserId": identifier(read_only=True),
            "itemType": enum(["task", "note", "file_link", "web_link", "idea", "assignment"]),
            "title": text(max_length=500, nullable=True),
            "content": text(max_length=20000, nullable=True),
            "rawUrl": text(format_name="uri", max_length=2048, nullable=True),
            "rawPath": text(max_length=4096, nullable=True),
            "status": enum(["unprocessed", "converted", "discarded"]),
            "convertedObjectId": identifier(nullable=True, read_only=True),
        },
        ["ownerUserId", "itemType", "status"],
    ),
    "Task": entity(
        {
            "projectId": identifier(nullable=True),
            "parentTaskId": identifier(nullable=True),
            "title": text(min_length=1, max_length=500),
            "description": text(max_length=50000, nullable=True),
            "authorUserId": identifier(),
            "requesterUserId": identifier(nullable=True),
            "primaryCounterpartyObjectId": identifier(nullable=True),
            "status": enum(["new", "in_progress", "review", "completed", "cancelled"]),
            "priority": enum(["low", "normal", "high", "critical"]),
            "scheduledDate": date(nullable=True),
            "startTimeLocal": text(format_name="time", nullable=True),
            "scheduleTimeZone": text(max_length=64, nullable=True),
            "startAtUtc": timestamp(nullable=True, read_only=True),
            "plannedDurationMinutes": integer(minimum=1, maximum=10080, nullable=True),
            "deadlineAt": timestamp(nullable=True),
            "assigneeIds": array(identifier(), max_items=100, unique=True),
            "watcherIds": array(identifier(), max_items=100, unique=True),
            "recurrenceSeriesId": identifier(nullable=True, read_only=True),
        },
        ["title", "authorUserId", "status", "priority", "assigneeIds", "watcherIds"],
    ),
    "Checklist": entity(
        {
            "taskId": identifier(),
            "title": text(min_length=1, max_length=300),
            "sortOrder": integer(),
            "items": array(reference("ChecklistItem"), max_items=500),
        },
        ["taskId", "title", "sortOrder", "items"],
    ),
    "ChecklistItem": entity(
        {
            "checklistId": identifier(),
            "text": text(min_length=1, max_length=1000),
            "isCompleted": boolean(),
            "completedBy": identifier(nullable=True),
            "completedAt": timestamp(nullable=True),
            "sortOrder": integer(),
        },
        ["checklistId", "text", "isCompleted", "sortOrder"],
    ),
    "RecurrenceSeries": entity(
        {
            "status": enum(["active", "paused", "completed", "cancelled"]),
            "frequency": enum(["daily", "weekly", "monthly", "yearly"]),
            "interval": integer(minimum=1, maximum=999),
            "weekdays": array(integer(minimum=1, maximum=7), max_items=7, unique=True),
            "monthDays": array(integer(minimum=-31, maximum=31), max_items=62, unique=True),
            "monthOfYear": integer(minimum=1, maximum=12, nullable=True),
            "occurrenceStartDate": date(),
            "localStartTime": text(format_name="time", nullable=True),
            "timeZone": text(min_length=1, max_length=64),
            "untilDate": date(nullable=True),
            "maxOccurrences": integer(minimum=1, nullable=True),
            "nextGenerationDate": date(read_only=True) if False else date(),
            "template": reference("RecurrenceTaskTemplate"),
        },
        [
            "status",
            "frequency",
            "interval",
            "occurrenceStartDate",
            "timeZone",
            "nextGenerationDate",
            "template",
        ],
    ),
    "RecurrenceOccurrence": entity(
        {
            "seriesId": identifier(),
            "occurrenceKey": text(min_length=1, max_length=64),
            "localDate": date(),
            "status": enum(["planned", "generated", "skipped", "cancelled"]),
            "taskId": identifier(nullable=True),
        },
        ["seriesId", "occurrenceKey", "localDate", "status"],
    ),
    "CalendarEvent": entity(
        {
            "projectId": identifier(nullable=True),
            "title": text(min_length=1, max_length=500),
            "description": text(max_length=20000, nullable=True),
            "eventDate": date(),
            "isAllDay": boolean(),
            "startAtUtc": timestamp(nullable=True),
            "endAtUtc": timestamp(nullable=True),
            "timeZone": text(max_length=64),
            "status": enum(["scheduled", "cancelled"]),
            "userAttendees": array(reference("EventAttendee"), max_items=500),
            "contactAttendees": array(reference("ContactAttendee"), max_items=500),
        },
        ["title", "eventDate", "isAllDay", "timeZone", "status", "userAttendees", "contactAttendees"],
    ),
    "EventAttendee": object_schema(
        {
            "userAccountId": identifier(),
            "role": enum(["required", "optional", "observer"]),
            "responseStatus": enum(["pending", "accepted", "declined", "tentative"]),
            "respondedAt": timestamp(nullable=True),
        },
        ["userAccountId", "role", "responseStatus"],
    ),
    "Reminder": entity(
        {
            "targetObjectId": identifier(),
            "recipientUserId": identifier(),
            "triggerType": enum(["absolute", "before_start", "before_deadline", "at_start", "at_deadline"]),
            "offsetMinutes": integer(minimum=0, maximum=525600, nullable=True),
            "absoluteTriggerAt": timestamp(nullable=True),
            "nextTriggerAt": timestamp(),
            "status": enum(["scheduled", "due", "delivered", "snoozed", "cancelled", "expired"]),
            "snoozedUntil": timestamp(nullable=True),
            "deliveredAt": timestamp(nullable=True),
        },
        ["targetObjectId", "recipientUserId", "triggerType", "nextTriggerAt", "status"],
    ),
    "ReminderOccurrence": entity(
        {
            "reminderId": identifier(),
            "dueAt": timestamp(),
            "status": enum(["created", "claimed", "delivered", "failed", "dead_letter", "cancelled"]),
            "attemptCount": integer(minimum=0),
            "nextAttemptAt": timestamp(),
        },
        ["reminderId", "dueAt", "status", "attemptCount", "nextAttemptAt"],
    ),
    "Notification": entity(
        {
            "recipientUserId": identifier(),
            "notificationType": text(min_length=1, max_length=40),
            "sourceObjectId": identifier(nullable=True),
            "title": text(min_length=1, max_length=500),
            "body": text(min_length=1, max_length=10000),
            "severity": enum(["info", "warning", "critical"]),
            "status": enum(["pending", "delivered", "read", "dismissed", "failed", "expired"]),
            "notBefore": timestamp(),
            "expiresAt": timestamp(nullable=True),
        },
        ["recipientUserId", "notificationType", "title", "body", "severity", "status", "notBefore"],
    ),
    "Comment": entity(
        {
            "targetObjectId": identifier(),
            "parentCommentId": identifier(nullable=True),
            "authorUserId": identifier(read_only=True),
            "body": text(min_length=1, max_length=20000),
            "status": enum(["active", "deleted"]),
            "deletedAt": timestamp(nullable=True, read_only=True),
        },
        ["targetObjectId", "authorUserId", "body", "status"],
    ),
    "Contact": entity(
        {
            "firstName": text(min_length=1, max_length=100),
            "lastName": text(max_length=100, nullable=True),
            "middleName": text(max_length=100, nullable=True),
            "displayName": text(min_length=1, max_length=300),
            "notes": text(max_length=20000, nullable=True),
            "status": enum(["active", "inactive"]),
        },
        ["firstName", "displayName", "status"],
    ),
    "Company": entity(
        {
            "name": text(min_length=1, max_length=500),
            "legalName": text(max_length=500, nullable=True),
            "industry": text(max_length=200, nullable=True),
            "website": text(format_name="uri", max_length=2048, nullable=True),
            "taxIdentifier": text(max_length=100, nullable=True),
            "notes": text(max_length=20000, nullable=True),
            "status": enum(["active", "inactive"]),
        },
        ["name", "status"],
    ),
    "ContactCompanyRole": entity(
        {
            "contactId": identifier(),
            "companyId": identifier(),
            "jobTitle": text(max_length=200, nullable=True),
            "departmentName": text(max_length=200, nullable=True),
            "isPrimary": boolean(),
            "validFrom": date(nullable=True),
            "validTo": date(nullable=True),
        },
        ["contactId", "companyId", "isPrimary"],
    ),
    "CommunicationChannel": entity(
        {
            "ownerObjectId": identifier(),
            "channelType": enum(["phone", "email", "telegram", "whatsapp", "viber", "other_messenger", "website"]),
            "label": text(max_length=100, nullable=True),
            "value": text(min_length=1, max_length=1000),
            "isPrimary": boolean(),
            "isVerified": boolean(),
        },
        ["ownerObjectId", "channelType", "value", "isPrimary", "isVerified"],
    ),
    "Address": entity(
        {
            "ownerObjectId": identifier(),
            "addressType": enum(["work", "legal", "postal", "other"]),
            "countryCode": text(min_length=2, max_length=2, nullable=True),
            "region": text(max_length=200, nullable=True),
            "city": text(max_length=200, nullable=True),
            "street": text(max_length=500, nullable=True),
            "postalCode": text(max_length=40, nullable=True),
            "formattedAddress": text(min_length=1, max_length=1000),
            "isPrimary": boolean(),
        },
        ["ownerObjectId", "addressType", "formattedAddress", "isPrimary"],
    ),
    "Interaction": entity(
        {
            "counterpartyObjectId": identifier(),
            "interactionType": enum(["call", "meeting", "email", "agreement", "note", "next_step"]),
            "occurredAt": timestamp(),
            "subject": text(min_length=1, max_length=500),
            "details": text(max_length=20000, nullable=True),
            "nextStep": text(max_length=5000, nullable=True),
            "nextStepDueAt": timestamp(nullable=True),
            "participantObjectIds": array(identifier(), max_items=500, unique=True),
        },
        ["counterpartyObjectId", "interactionType", "occurredAt", "subject", "participantObjectIds"],
    ),
    "CatalogItem": entity(
        {
            "parentId": identifier(nullable=True),
            "itemType": enum(["virtual_folder", "file_reference", "folder_reference", "web_link", "text_note"]),
            "name": text(min_length=1, max_length=500),
            "description": text(max_length=20000, nullable=True),
            "noteContent": text(max_length=100000, nullable=True),
            "webUrl": text(format_name="uri", max_length=2048, nullable=True),
            "mimeType": text(max_length=200, nullable=True),
            "fileExtension": text(max_length=32, nullable=True),
            "sortOrder": integer(),
        },
        ["itemType", "name", "sortOrder"],
    ),
    "FileLocation": entity(
        {
            "catalogItemId": identifier(),
            "locationType": enum(["local_path", "unc_path", "mapped_drive"]),
            "displayPath": text(min_length=1, max_length=4096, read_only=True),
            "rawPath": text(min_length=1, max_length=4096),
            "deviceId": identifier(nullable=True),
            "networkResourceId": identifier(nullable=True),
            "ownerUserId": identifier(read_only=True),
            "priority": integer(minimum=0, maximum=32767),
            "isEnabled": boolean(),
            "isPrimary": boolean(),
            "deviceAvailability": array(reference("FileLocationDeviceState"), max_items=500, read_only=True)
            if False
            else array(reference("FileLocationDeviceState"), max_items=500),
        },
        ["catalogItemId", "locationType", "displayPath", "ownerUserId", "priority", "isEnabled", "isPrimary"],
    ),
    "NetworkResource": entity(
        {
            "name": text(min_length=1, max_length=300),
            "rootUncPath": text(min_length=3, max_length=4096, pattern=r"^\\\\"),
            "status": enum(["active", "degraded", "unavailable", "retired"]),
            "allowWriteMetadata": boolean(),
            "lastHealthAt": timestamp(nullable=True, read_only=True),
        },
        ["name", "rootUncPath", "status", "allowWriteMetadata"],
    ),
    "Tag": entity(
        {
            "name": text(min_length=1, max_length=100),
            "colorCode": text(max_length=9, pattern=r"^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$", nullable=True),
            "description": text(max_length=2000, nullable=True),
        },
        ["name"],
    ),
    "ObjectLink": entity(
        {
            "sourceObjectId": identifier(),
            "targetObjectId": identifier(),
            "linkType": enum(
                [
                    "related",
                    "task_file",
                    "project_file",
                    "contact_file",
                    "task_contact",
                    "project_contact",
                    "task_project",
                    "parent_reference",
                ]
            ),
        },
        ["sourceObjectId", "targetObjectId", "linkType"],
    ),
    "FeatureFlag": entity(
        {
            "key": text(min_length=1, max_length=100),
            "enabled": boolean(),
            "minimumClientVersion": text(max_length=32, nullable=True),
            "configuration": bounded_json_object("Flag-specific validated configuration."),
        },
        ["key", "enabled", "configuration"],
    ),
    "BackgroundJobRun": entity(
        {
            "jobCode": text(min_length=1, max_length=100),
            "status": enum(["queued", "running", "succeeded", "failed", "dead_letter", "cancelled"]),
            "attempt": integer(minimum=0),
            "scheduledAt": timestamp(),
            "startedAt": timestamp(nullable=True),
            "finishedAt": timestamp(nullable=True),
            "errorCode": text(max_length=100, nullable=True),
        },
        ["jobCode", "status", "attempt", "scheduledAt"],
    ),
    "BackupRun": entity(
        {
            "backupType": enum(["base", "incremental", "wal_archive", "config", "restore_test"]),
            "status": enum(["running", "succeeded", "failed", "cancelled"]),
            "startedAt": timestamp(),
            "finishedAt": timestamp(nullable=True),
            "encrypted": boolean(),
            "sizeBytes": integer(minimum=0, int_format="int64", nullable=True),
            "checksum": text(max_length=200, nullable=True),
        },
        ["backupType", "status", "startedAt", "encrypted"],
    ),
}


ENTITIES.update(
    {
        "Permission": object_schema(
            {
                "code": text(min_length=1, max_length=100),
                "resource": text(min_length=1, max_length=60),
                "action": text(min_length=1, max_length=60),
                "description": text(min_length=1, max_length=1000),
                "sensitive": boolean(),
            },
            ["code", "resource", "action", "description", "sensitive"],
        ),
        "UserRole": object_schema(
            {
                "id": identifier(),
                "roleId": identifier(),
                "roleCode": text(max_length=60),
                "departmentId": identifier(nullable=True),
                "validFrom": timestamp(),
                "validUntil": timestamp(nullable=True),
            },
            ["id", "roleId", "roleCode", "validFrom"],
        ),
        "ContactAttendee": object_schema(
            {
                "contactId": identifier(),
                "role": enum(["required", "optional", "observer"]),
                "responseStatus": enum(["pending", "accepted", "declined", "tentative"]),
                "respondedAt": timestamp(nullable=True),
            },
            ["contactId", "role", "responseStatus"],
        ),
        "FileLocationDeviceState": object_schema(
            {
                "deviceId": identifier(),
                "userAccountId": identifier(),
                "status": enum(
                    [
                        "unknown",
                        "available",
                        "not_found",
                        "access_denied",
                        "resource_unavailable",
                        "invalid_path",
                        "timeout",
                    ]
                ),
                "lastCheckedAt": timestamp(nullable=True),
                "latencyMs": integer(minimum=0, nullable=True),
                "version": integer(minimum=1, int_format="int64"),
            },
            ["deviceId", "userAccountId", "status", "version"],
        ),
        "RecurrenceTaskTemplate": object_schema(
            {
                "projectId": identifier(nullable=True),
                "title": text(min_length=1, max_length=500),
                "description": text(max_length=50000, nullable=True),
                "authorUserId": identifier(),
                "requesterUserId": identifier(nullable=True),
                "primaryCounterpartyObjectId": identifier(nullable=True),
                "priority": enum(["low", "normal", "high", "critical"]),
                "plannedDurationMinutes": integer(minimum=1, maximum=10080, nullable=True),
                "deadlineOffsetMinutes": integer(nullable=True),
                "assigneeIds": array(identifier(), max_items=100, unique=True),
                "watcherIds": array(identifier(), max_items=100, unique=True),
                "checklists": array(reference("RecurrenceTemplateChecklist"), max_items=50),
                "reminderRules": array(reference("RecurrenceTemplateReminderRule"), max_items=50),
                "templateVersion": integer(minimum=1, int_format="int64"),
            },
            [
                "title",
                "authorUserId",
                "priority",
                "assigneeIds",
                "watcherIds",
                "checklists",
                "reminderRules",
                "templateVersion",
            ],
        ),
        "RecurrenceTemplateChecklist": object_schema(
            {
                "id": identifier(),
                "title": text(min_length=1, max_length=300),
                "sortOrder": integer(),
                "items": array(reference("RecurrenceTemplateChecklistItem"), max_items=500),
            },
            ["id", "title", "sortOrder", "items"],
        ),
        "RecurrenceTemplateChecklistItem": object_schema(
            {
                "id": identifier(),
                "text": text(min_length=1, max_length=1000),
                "sortOrder": integer(),
            },
            ["id", "text", "sortOrder"],
        ),
        "RecurrenceTemplateReminderRule": object_schema(
            {
                "id": identifier(),
                "recipientUserId": identifier(nullable=True),
                "triggerType": enum(["before_start", "before_deadline", "at_start", "at_deadline"]),
                "offsetMinutes": integer(minimum=0, nullable=True),
            },
            ["id", "triggerType"],
        ),
    }
)


CREATE_REQUIRED: dict[str, list[str]] = {
    "User": ["firstName", "lastName", "login"],
    "Department": ["code", "name"],
    "Role": ["code", "name", "scopeType"],
    "Project": ["name", "ownerUserId"],
    "ProjectMember": ["userAccountId", "projectRoleId"],
    "InboxItem": ["itemType"],
    "Task": ["title", "authorUserId"],
    "Checklist": ["title"],
    "ChecklistItem": ["text"],
    "RecurrenceSeries": ["frequency", "interval", "occurrenceStartDate", "timeZone", "template"],
    "CalendarEvent": ["title", "eventDate", "isAllDay", "timeZone"],
    "Reminder": ["targetObjectId", "recipientUserId", "triggerType"],
    "Comment": ["body"],
    "Contact": ["firstName", "displayName"],
    "Company": ["name"],
    "ContactCompanyRole": ["contactId", "companyId"],
    "CommunicationChannel": ["channelType", "value"],
    "Address": ["addressType", "formattedAddress"],
    "Interaction": ["counterpartyObjectId", "interactionType", "occurredAt", "subject"],
    "CatalogItem": ["itemType", "name"],
    "FileLocation": ["locationType", "rawPath"],
    "NetworkResource": ["name", "rootUncPath"],
    "Tag": ["name"],
    "ObjectLink": ["sourceObjectId", "targetObjectId", "linkType"],
}


def writable_properties(entity_schema: dict[str, Any]) -> dict[str, Any]:
    excluded = {"id", "organizationId", "version", "createdAt", "updatedAt"}
    properties: dict[str, Any] = {}
    for name, schema in entity_schema["properties"].items():
        if name in excluded or schema.get("readOnly"):
            continue
        properties[name] = copy.deepcopy(schema)
    return properties


def action_schemas() -> dict[str, dict[str, Any]]:
    string_codes = array(text(min_length=1, max_length=100), max_items=200, unique=True)
    expected_version = integer(minimum=1, int_format="int64")
    return {
        "ProblemDetails": object_schema(
            {
                "type": text(format_name="uri-reference"),
                "title": text(min_length=1, max_length=300),
                "status": integer(minimum=400, maximum=599),
                "detail": text(max_length=4000, nullable=True),
                "instance": text(max_length=2048, nullable=True),
                "code": text(min_length=1, max_length=100),
                "traceId": text(min_length=1, max_length=100),
                "correlationId": identifier(),
                "fieldErrors": array(reference("FieldError"), max_items=100),
                "currentVersion": integer(minimum=1, int_format="int64", nullable=True),
                "currentEtag": text(max_length=100, nullable=True),
                "retryAfterSeconds": integer(minimum=0, nullable=True),
            },
            ["type", "title", "status", "code", "traceId", "correlationId", "fieldErrors"],
        ),
        "FieldError": object_schema(
            {
                "path": text(min_length=1, max_length=500),
                "code": text(min_length=1, max_length=100),
                "message": text(min_length=1, max_length=1000),
            },
            ["path", "code", "message"],
        ),
        "ActionResult": object_schema(
            {
                "operationId": identifier(),
                "status": enum(["accepted", "completed", "rejected"]),
                "resourceId": identifier(nullable=True),
            },
            ["operationId", "status"],
        ),
        "ActivationRequest": object_schema({"expectedVersion": expected_version}, ["expectedVersion"]),
        "AdminResetPasswordRequest": object_schema(
            {
                "targetUserId": identifier(),
                "temporaryPassword": text(min_length=12, max_length=1024, write_only=True),
                "expectedVersion": expected_version,
            },
            ["targetUserId", "temporaryPassword", "expectedVersion"],
        ),
        "ArchiveRequest": object_schema({"reason": text(max_length=2000, nullable=True)}, min_properties=0),
        "AttendeeResponse": object_schema(
            {
                "responseStatus": enum(["accepted", "declined", "tentative"]),
                "expectedAttendeeVersion": expected_version,
            },
            ["responseStatus", "expectedAttendeeVersion"],
        ),
        "AttendeesReplace": object_schema(
            {
                "users": array(reference("EventAttendee"), max_items=500, unique=False),
                "contacts": array(reference("ContactAttendee"), max_items=500, unique=False),
            },
            ["users", "contacts"],
        ),
        "BackupRequest": object_schema(
            {"backupType": enum(["base", "incremental", "config"])},
            ["backupType"],
        ),
        "BackupVerifyRequest": object_schema(
            {"backupRunId": identifier(), "deepVerify": boolean()},
            ["backupRunId", "deepVerify"],
        ),
        "BlockUserRequest": object_schema(
            {"reason": text(min_length=1, max_length=2000), "expectedVersion": expected_version},
            ["reason", "expectedVersion"],
        ),
        "BulkOperationItemResult": object_schema(
            {
                "itemId": identifier(),
                "status": enum(["succeeded", "failed", "skipped"]),
                "code": text(max_length=100, nullable=True),
                "version": integer(minimum=1, int_format="int64", nullable=True),
            },
            ["itemId", "status"],
        ),
        "BulkOperationResult": object_schema(
            {"items": array(reference("BulkOperationItemResult"), min_items=1, max_items=500)},
            ["items"],
        ),
        "BulkTaskTransition": object_schema(
            {
                "items": array(
                    object_schema(
                        {
                            "taskId": identifier(),
                            "expectedVersion": expected_version,
                            "targetStatus": enum(["new", "in_progress", "review", "completed", "cancelled"]),
                        },
                        ["taskId", "expectedVersion", "targetStatus"],
                    ),
                    min_items=1,
                    max_items=500,
                )
            },
            ["items"],
        ),
        "ChangePasswordRequest": object_schema(
            {
                "currentPassword": text(min_length=1, max_length=1024, write_only=True),
                "newPassword": text(min_length=12, max_length=1024, write_only=True),
            },
            ["currentPassword", "newPassword"],
        ),
        "CatalogMoveRequest": object_schema(
            {
                "parentCatalogItemId": identifier(nullable=True),
                "sortOrder": integer(),
                "expectedParentVersion": expected_version,
            },
            ["sortOrder", "expectedParentVersion"],
        ),
        "CompactionRequest": object_schema(
            {"relation": text(min_length=1, max_length=200), "before": timestamp()},
            ["relation", "before"],
        ),
        "CurrentSession": object_schema(
            {
                "sessionId": identifier(),
                "organizationId": identifier(),
                "user": reference("User"),
                "device": reference("Device"),
                "permissionCodes": string_codes,
                "capabilities": string_codes,
                "scopeVersion": integer(minimum=1, int_format="int64"),
                "accessExpiresAt": timestamp(),
            },
            [
                "sessionId",
                "organizationId",
                "user",
                "device",
                "permissionCodes",
                "capabilities",
                "scopeVersion",
                "accessExpiresAt",
            ],
        ),
        "DeactivateUserRequest": object_schema(
            {"reason": text(min_length=1, max_length=2000), "expectedVersion": expected_version},
            ["reason", "expectedVersion"],
        ),
        "DeletionReceipt": object_schema(
            {
                "objectId": identifier(),
                "objectType": text(min_length=1, max_length=40),
                "deletedAt": timestamp(),
                "purgeAfter": timestamp(),
                "version": integer(minimum=1, int_format="int64"),
            },
            ["objectId", "objectType", "deletedAt", "purgeAfter", "version"],
        ),
        "DepartmentManagersReplace": object_schema(
            {"managerUserIds": array(identifier(), max_items=100, unique=True)},
            ["managerUserIds"],
        ),
        "DeviceHeartbeat": object_schema(
            {
                "appVersion": text(min_length=1, max_length=32),
                "osVersion": text(max_length=100),
                "observedAt": timestamp(),
            },
            ["appVersion", "observedAt"],
        ),
        "DismissReminderRequest": object_schema(
            {"expectedVersion": expected_version},
            ["expectedVersion"],
        ),
        "EffectivePermissions": object_schema(
            {
                "objectId": identifier(),
                "allowed": string_codes,
                "denied": string_codes,
                "scopeVersion": integer(minimum=1, int_format="int64"),
                "explanationId": identifier(),
            },
            ["objectId", "allowed", "denied", "scopeVersion", "explanationId"],
        ),
        "ExportJob": object_schema(
            {
                "jobId": identifier(),
                "status": enum(["queued", "running", "succeeded", "failed", "expired"]),
                "downloadUrl": text(format_name="uri", nullable=True),
                "expiresAt": timestamp(nullable=True),
            },
            ["jobId", "status"],
        ),
        "FileLocationCheckCreate": object_schema(
            {
                "deviceId": identifier(),
                "status": enum(
                    [
                        "available",
                        "not_found",
                        "access_denied",
                        "resource_unavailable",
                        "invalid_path",
                        "timeout",
                    ]
                ),
                "latencyMs": integer(minimum=0, nullable=True),
                "osErrorCode": text(max_length=80, nullable=True),
                "checkedAt": timestamp(),
                "expectedLocationVersion": expected_version,
            },
            ["deviceId", "status", "checkedAt", "expectedLocationVersion"],
        ),
        "GenerateOccurrencesRequest": object_schema(
            {"throughDate": date(), "expectedSeriesVersion": expected_version},
            ["throughDate", "expectedSeriesVersion"],
        ),
        "GenerationSummary": object_schema(
            {
                "seriesId": identifier(),
                "generatedCount": integer(minimum=0),
                "skippedCount": integer(minimum=0),
                "throughDate": date(),
                "seriesVersion": integer(minimum=1, int_format="int64"),
            },
            ["seriesId", "generatedCount", "skippedCount", "throughDate", "seriesVersion"],
        ),
        "InboxConvertCatalog": object_schema(
            {
                "itemType": enum(["file_reference", "folder_reference", "web_link", "text_note"]),
                "parentCatalogItemId": identifier(nullable=True),
                "expectedInboxVersion": expected_version,
            },
            ["itemType", "expectedInboxVersion"],
        ),
        "InboxConvertTask": object_schema(
            {
                "projectId": identifier(nullable=True),
                "scheduledDate": date(nullable=True),
                "expectedInboxVersion": expected_version,
            },
            ["expectedInboxVersion"],
        ),
        "InteractionParticipants": object_schema(
            {"participantObjectIds": array(identifier(), max_items=500, unique=True)},
            ["participantObjectIds"],
        ),
        "JobRunRequest": object_schema(
            {
                "jobCode": text(min_length=1, max_length=100),
                "input": bounded_json_object("Job-specific schema-validated input."),
            },
            ["jobCode", "input"],
        ),
        "LoginRequest": object_schema(
            {
                "login": text(min_length=3, max_length=100),
                "password": text(min_length=1, max_length=1024, write_only=True),
                "device": reference("DeviceRegistration"),
            },
            ["login", "password", "device"],
        ),
        "DeviceRegistration": object_schema(
            {
                "deviceKey": text(min_length=16, max_length=200, write_only=True),
                "deviceName": text(min_length=1, max_length=200),
                "platform": enum(["windows", "linux", "macos"]),
                "appVersion": text(min_length=1, max_length=32),
                "osVersion": text(max_length=100, nullable=True),
            },
            ["deviceKey", "deviceName", "platform", "appVersion"],
        ),
        "LogoutAllRequest": object_schema(
            {"keepCurrentSession": boolean()},
            ["keepCurrentSession"],
        ),
        "MaintenanceModeRequest": object_schema(
            {
                "enabled": boolean(),
                "reason": text(min_length=1, max_length=2000),
                "expectedVersion": expected_version,
            },
            ["enabled", "reason", "expectedVersion"],
        ),
        "NotificationActionRequest": object_schema(
            {
                "action": enum(["mark_read", "dismiss"]),
                "expectedVersion": expected_version,
            },
            ["action", "expectedVersion"],
        ),
        "OrderKeys": object_schema(
            {
                "items": array(
                    object_schema(
                        {"id": identifier(), "sortOrder": integer(), "expectedVersion": expected_version},
                        ["id", "sortOrder", "expectedVersion"],
                    ),
                    min_items=1,
                    max_items=500,
                )
            },
            ["items"],
        ),
        "PermissionCodes": object_schema({"codes": string_codes}, ["codes"]),
        "PermissionOverrides": object_schema(
            {
                "allow": string_codes,
                "deny": string_codes,
                "expectedMemberVersion": expected_version,
            },
            ["allow", "deny", "expectedMemberVersion"],
        ),
        "ProbeRequest": object_schema(
            {"deviceId": identifier(), "timeoutSeconds": integer(minimum=1, maximum=60)},
            ["deviceId", "timeoutSeconds"],
        ),
        "ProbeResult": object_schema(
            {
                "resourceId": identifier(),
                "deviceId": identifier(),
                "status": enum(["available", "not_found", "access_denied", "timeout", "invalid_path"]),
                "latencyMs": integer(minimum=0, nullable=True),
                "checkedAt": timestamp(),
            },
            ["resourceId", "deviceId", "status", "checkedAt"],
        ),
        "PurgeRequest": object_schema(
            {
                "reason": text(min_length=1, max_length=2000),
                "expectedVersion": expected_version,
                "confirmObjectId": identifier(),
            },
            ["reason", "expectedVersion", "confirmObjectId"],
        ),
        "ReadAllRequest": object_schema(
            {"notificationIds": array(identifier(), min_items=1, max_items=500, unique=True)},
            ["notificationIds"],
        ),
        "ReadAllResult": object_schema(
            {"updatedCount": integer(minimum=0), "latestVersion": integer(minimum=1, int_format="int64")},
            ["updatedCount", "latestVersion"],
        ),
        "RealtimeNegotiation": object_schema(
            {
                "url": text(format_name="uri"),
                "accessToken": text(min_length=20, max_length=4096, read_only=True),
                "expiresAt": timestamp(),
                "protocols": array(enum(["json", "messagepack"]), min_items=1, max_items=2),
            },
            ["url", "accessToken", "expiresAt", "protocols"],
        ),
        "RecurrenceChangeResult": object_schema(
            {
                "series": reference("RecurrenceSeries"),
                "changedTaskIds": array(identifier(), max_items=500),
                "regeneratedOccurrenceCount": integer(minimum=0),
            },
            ["series", "changedTaskIds", "regeneratedOccurrenceCount"],
        ),
        "RecurrencePreviewRequest": object_schema(
            {
                "rule": reference("RecurrenceSeriesCreate"),
                "fromDate": date(),
                "limit": integer(minimum=1, maximum=500),
            },
            ["rule", "fromDate", "limit"],
        ),
        "RecurrenceScopedChange": object_schema(
            {
                "scope": enum(["this_occurrence", "this_and_future", "entire_series"]),
                "patch": reference("TaskPatch"),
                "expectedTaskVersion": expected_version,
            },
            ["scope", "patch", "expectedTaskVersion"],
        ),
        "RefreshRequest": object_schema(
            {
                "refreshToken": text(min_length=32, max_length=4096, write_only=True),
                "deviceKey": text(min_length=16, max_length=200, write_only=True),
            },
            ["refreshToken", "deviceKey"],
        ),
        "ReindexRequest": object_schema(
            {"objectTypes": array(text(max_length=40), max_items=50, unique=True)},
            ["objectTypes"],
        ),
        "ResolveLocationRequest": object_schema(
            {
                "deviceId": identifier(),
                "includeUnavailable": boolean(),
            },
            ["deviceId", "includeUnavailable"],
        ),
        "ResolvedLocation": object_schema(
            {
                "catalogItemId": identifier(),
                "locationId": identifier(),
                "displayPath": text(min_length=1, max_length=4096),
                "rawPath": text(min_length=1, max_length=4096, nullable=True),
                "rawPathVisible": boolean(),
                "availability": enum(["unknown", "available", "not_found", "access_denied", "resource_unavailable"]),
                "version": integer(minimum=1, int_format="int64"),
            },
            ["catalogItemId", "locationId", "displayPath", "rawPathVisible", "availability", "version"],
        ),
        "RestoreBackupRequest": object_schema(
            {
                "backupRunId": identifier(),
                "restorePlanId": identifier(),
                "confirmationCode": text(min_length=12, max_length=200),
            },
            ["backupRunId", "restorePlanId", "confirmationCode"],
        ),
        "RestorePlan": object_schema(
            {
                "id": identifier(),
                "backupRunId": identifier(),
                "status": enum(["draft", "validated", "approved", "executing", "completed", "failed"]),
                "steps": array(text(min_length=1, max_length=1000), min_items=1, max_items=100),
                "expiresAt": timestamp(),
            },
            ["id", "backupRunId", "status", "steps", "expiresAt"],
        ),
        "RestoreRequest": object_schema(
            {"reason": text(max_length=2000, nullable=True), "expectedVersion": expected_version},
            ["expectedVersion"],
        ),
        "RevocationSummary": object_schema(
            {
                "revokedSessions": integer(minimum=0),
                "revokedRefreshTokens": integer(minimum=0),
                "effectiveAt": timestamp(),
            },
            ["revokedSessions", "revokedRefreshTokens", "effectiveAt"],
        ),
        "RevokeDeviceRequest": object_schema(
            {"reason": text(min_length=1, max_length=2000), "expectedVersion": expected_version},
            ["reason", "expectedVersion"],
        ),
        "SessionTokens": object_schema(
            {
                "accessToken": text(min_length=20, max_length=4096, read_only=True),
                "accessExpiresAt": timestamp(read_only=True),
                "refreshToken": text(min_length=32, max_length=4096, read_only=True),
                "refreshExpiresAt": timestamp(read_only=True),
                "sessionId": identifier(read_only=True),
            },
            ["accessToken", "accessExpiresAt", "refreshToken", "refreshExpiresAt", "sessionId"],
        ),
        "SkipOccurrenceRequest": object_schema(
            {"reason": text(max_length=2000, nullable=True), "expectedSeriesVersion": expected_version},
            ["expectedSeriesVersion"],
        ),
        "SnoozeRequest": object_schema(
            {
                "until": timestamp(),
                "expectedVersion": expected_version,
            },
            ["until", "expectedVersion"],
        ),
        "SyncAck": object_schema(
            {
                "deviceId": identifier(),
                "acknowledgedSequence": integer(minimum=0, int_format="int64"),
                "scopeVersion": integer(minimum=1, int_format="int64"),
            },
            ["deviceId", "acknowledgedSequence", "scopeVersion"],
        ),
        "SyncBootstrapRequest": object_schema(
            {
                "deviceId": identifier(),
                "snapshotSessionId": identifier(nullable=True),
                "dataset": text(max_length=40, nullable=True),
                "afterOrdinal": integer(minimum=0, int_format="int64", nullable=True),
                "pageSize": integer(minimum=1, maximum=500),
                "cacheSchemaVersion": integer(minimum=1),
            },
            ["deviceId", "pageSize", "cacheSchemaVersion"],
        ),
        "TaskAssigneesReplace": object_schema(
            {
                "assigneeIds": array(identifier(), max_items=100, unique=True),
                "primaryAssigneeId": identifier(nullable=True),
            },
            ["assigneeIds"],
        ),
        "TaskDependency": object_schema(
            {
                "predecessorTaskId": identifier(),
                "successorTaskId": identifier(),
                "dependencyType": enum(["finish_to_start", "start_to_start"]),
            },
            ["predecessorTaskId", "successorTaskId", "dependencyType"],
        ),
        "TaskDependencyCreate": object_schema(
            {
                "predecessorTaskId": identifier(),
                "dependencyType": enum(["finish_to_start", "start_to_start"]),
                "expectedPredecessorVersion": expected_version,
            },
            ["predecessorTaskId", "dependencyType", "expectedPredecessorVersion"],
        ),
        "TaskMoveRequest": object_schema(
            {
                "parentTaskId": identifier(nullable=True),
                "projectId": identifier(nullable=True),
                "sortOrder": integer(),
                "expectedParentVersion": expected_version,
            },
            ["sortOrder", "expectedParentVersion"],
        ),
        "TaskTransitionRequest": object_schema(
            {
                "targetStatus": enum(["new", "in_progress", "review", "completed", "cancelled"]),
                "reason": text(max_length=2000, nullable=True),
            },
            ["targetStatus"],
        ),
        "TaskWatchersReplace": object_schema(
            {"watcherIds": array(identifier(), max_items=100, unique=True)},
            ["watcherIds"],
        ),
        "TemporaryCredentialReceipt": object_schema(
            {
                "userId": identifier(),
                "temporaryPassword": text(min_length=12, max_length=1024, read_only=True),
                "expiresAt": timestamp(),
                "mustChangePassword": boolean(),
            },
            ["userId", "temporaryPassword", "expiresAt", "mustChangePassword"],
        ),
        "TransferOwnershipRequest": object_schema(
            {
                "newOwnerUserId": identifier(),
                "expectedNewOwnerMembershipVersion": expected_version,
            },
            ["newOwnerUserId", "expectedNewOwnerMembershipVersion"],
        ),
        "UserRolesReplace": object_schema(
            {
                "roles": array(
                    object_schema(
                        {
                            "roleId": identifier(),
                            "departmentId": identifier(nullable=True),
                            "validUntil": timestamp(nullable=True),
                        },
                        ["roleId"],
                    ),
                    max_items=100,
                ),
                "expectedUserVersion": expected_version,
            },
            ["roles", "expectedUserVersion"],
        ),
    }


def supplemental_schemas() -> dict[str, dict[str, Any]]:
    return {
        "OrganizationSettings": object_schema(
            {
                "trashRetentionDays": integer(minimum=1, maximum=3650),
                "historyRetentionDays": integer(minimum=90, maximum=36500),
                "changeFeedRetentionDays": integer(minimum=7, maximum=3650),
                "recurrenceHorizonDays": integer(minimum=7, maximum=730),
                "maxRequestBytes": integer(minimum=65536, maximum=10485760),
                "version": integer(minimum=1, int_format="int64"),
            },
            [
                "trashRetentionDays",
                "historyRetentionDays",
                "changeFeedRetentionDays",
                "recurrenceHorizonDays",
                "maxRequestBytes",
                "version",
            ],
        ),
        "UserSettings": object_schema(
            {
                "language": text(min_length=2, max_length=16),
                "timeFormat": enum(["12h", "24h"]),
                "firstDayOfWeek": integer(minimum=1, maximum=7),
                "workdayStart": text(format_name="time"),
                "workdayEnd": text(format_name="time"),
                "weekendDays": array(integer(minimum=1, maximum=7), min_items=1, max_items=6, unique=True),
                "defaultTaskDurationMinutes": integer(minimum=5, maximum=1440),
                "defaultReminderOffsetMinutes": integer(minimum=0, maximum=525600),
                "autostartEnabled": boolean(),
                "allowLocalPaths": boolean(),
                "confirmCatalogDelete": boolean(),
                "missingFileBehavior": enum(["show_actions", "keep_inactive", "prompt_relink"]),
                "version": integer(minimum=1, int_format="int64"),
            },
            [
                "language",
                "timeFormat",
                "firstDayOfWeek",
                "workdayStart",
                "workdayEnd",
                "weekendDays",
                "defaultTaskDurationMinutes",
                "defaultReminderOffsetMinutes",
                "autostartEnabled",
                "allowLocalPaths",
                "confirmCatalogDelete",
                "missingFileBehavior",
                "version",
            ],
        ),
        "NotificationPreferences": object_schema(
            {
                "notificationType": text(min_length=1, max_length=40),
                "enabled": boolean(),
                "desktopEnabled": boolean(),
                "soundEnabled": boolean(),
                "defaultSnoozeMinutes": integer(minimum=1, maximum=10080),
                "quietHoursStart": text(format_name="time", nullable=True),
                "quietHoursEnd": text(format_name="time", nullable=True),
                "quietHoursTimeZone": text(max_length=64, nullable=True),
                "version": integer(minimum=1, int_format="int64"),
            },
            [
                "notificationType",
                "enabled",
                "desktopEnabled",
                "soundEnabled",
                "defaultSnoozeMinutes",
                "version",
            ],
        ),
        "ObjectReference": object_schema(
            {
                "id": identifier(),
                "objectType": text(min_length=1, max_length=40),
                "title": text(min_length=1, max_length=500),
                "version": integer(minimum=1, int_format="int64"),
            },
            ["id", "objectType", "title", "version"],
        ),
        "HistoryVersion": object_schema(
            {
                "objectId": identifier(),
                "objectVersion": integer(minimum=1, int_format="int64"),
                "changeType": enum(["created", "updated", "state_changed", "archived", "restored", "trashed", "purged"]),
                "changedAt": timestamp(),
                "changedBy": identifier(nullable=True),
                "changedFields": array(text(max_length=200), max_items=500, unique=True),
                "correlationId": identifier(),
            },
            ["objectId", "objectVersion", "changeType", "changedAt", "changedFields", "correlationId"],
        ),
        "ArchiveEntry": object_schema(
            {
                "object": reference("ObjectReference"),
                "archivedBy": identifier(),
                "archivedAt": timestamp(),
                "reason": text(max_length=2000, nullable=True),
                "status": enum(["archived", "restored"]),
            },
            ["object", "archivedBy", "archivedAt", "status"],
        ),
        "TrashEntry": object_schema(
            {
                "object": reference("ObjectReference"),
                "deletedBy": identifier(),
                "deletedAt": timestamp(),
                "purgeAfter": timestamp(),
                "status": enum(["retained", "restored", "purged", "blocked_by_hold"]),
            },
            ["object", "deletedBy", "deletedAt", "purgeAfter", "status"],
        ),
        "AuditEntry": object_schema(
            {
                "id": identifier(),
                "occurredAt": timestamp(),
                "actorUserId": identifier(nullable=True),
                "actionCode": text(min_length=1, max_length=100),
                "objectId": identifier(nullable=True),
                "outcome": enum(["success", "denied", "failure"]),
                "reasonCode": text(max_length=100, nullable=True),
                "correlationId": identifier(),
            },
            ["id", "occurredAt", "actionCode", "outcome", "correlationId"],
        ),
        "HistoryEntry": object_schema(
            {
                "id": identifier(),
                "objectId": identifier(),
                "objectVersion": integer(minimum=1, int_format="int64"),
                "changedAt": timestamp(),
                "changeType": text(min_length=1, max_length=24),
                "changedFields": array(text(max_length=200), max_items=500),
                "correlationId": identifier(),
            },
            ["id", "objectId", "objectVersion", "changedAt", "changeType", "changedFields", "correlationId"],
        ),
        "CommentVersion": object_schema(
            {
                "version": integer(minimum=1, int_format="int64"),
                "body": text(min_length=1, max_length=20000),
                "changedBy": identifier(),
                "changedAt": timestamp(),
            },
            ["version", "body", "changedBy", "changedAt"],
        ),
        "LoginAttempt": object_schema(
            {
                "id": identifier(),
                "login": text(max_length=100),
                "userAccountId": identifier(nullable=True),
                "deviceId": identifier(nullable=True),
                "ipAddress": text(format_name="ipv4", nullable=True),
                "occurredAt": timestamp(),
                "succeeded": boolean(),
                "failureCode": text(max_length=40, nullable=True),
                "correlationId": identifier(),
            },
            ["id", "login", "occurredAt", "succeeded", "correlationId"],
        ),
        "Session": object_schema(
            {
                "id": identifier(),
                "userAccountId": identifier(),
                "deviceId": identifier(),
                "status": enum(["active", "revoked", "expired"]),
                "createdAt": timestamp(),
                "lastSeenAt": timestamp(),
                "idleExpiresAt": timestamp(),
                "absoluteExpiresAt": timestamp(),
            },
            ["id", "userAccountId", "deviceId", "status", "createdAt", "lastSeenAt", "idleExpiresAt", "absoluteExpiresAt"],
        ),
        "BackgroundJob": object_schema(
            {
                "id": identifier(),
                "jobCode": text(min_length=1, max_length=100),
                "scheduleKind": enum(["cron", "interval", "event", "continuous"]),
                "scheduleExpression": text(max_length=500, nullable=True),
                "enabled": boolean(),
                "maxParallelism": integer(minimum=1, maximum=32),
                "maxAttempts": integer(minimum=1, maximum=50),
                "timeoutSeconds": integer(minimum=1, maximum=86400),
                "version": integer(minimum=1, int_format="int64"),
            },
            ["id", "jobCode", "scheduleKind", "enabled", "maxParallelism", "maxAttempts", "timeoutSeconds", "version"],
        ),
        "Health": object_schema(
            {
                "status": enum(["healthy", "degraded", "unhealthy"]),
                "checkedAt": timestamp(),
                "version": text(min_length=1, max_length=32),
            },
            ["status", "checkedAt", "version"],
        ),
        "HealthDetails": object_schema(
            {
                "status": enum(["healthy", "degraded", "unhealthy"]),
                "checks": array(
                    object_schema(
                        {
                            "name": text(min_length=1, max_length=100),
                            "status": enum(["healthy", "degraded", "unhealthy"]),
                            "latencyMs": integer(minimum=0),
                            "code": text(max_length=100, nullable=True),
                        },
                        ["name", "status", "latencyMs"],
                    ),
                    max_items=100,
                ),
                "checkedAt": timestamp(),
            },
            ["status", "checks", "checkedAt"],
        ),
        "ServerTime": object_schema(
            {"utc": timestamp(), "monotonicSequence": integer(minimum=0, int_format="int64")},
            ["utc", "monotonicSequence"],
        ),
        "SystemVersion": object_schema(
            {
                "serverVersion": text(min_length=1, max_length=32),
                "apiVersion": text(min_length=1, max_length=16),
                "databaseSchemaVersion": integer(minimum=1),
                "minimumDesktopVersion": text(min_length=1, max_length=32),
            },
            ["serverVersion", "apiVersion", "databaseSchemaVersion", "minimumDesktopVersion"],
        ),
        "StorageStatus": object_schema(
            {
                "databaseBytes": integer(minimum=0, int_format="int64"),
                "freeBytes": integer(minimum=0, int_format="int64"),
                "walBytes": integer(minimum=0, int_format="int64"),
                "checkedAt": timestamp(),
            },
            ["databaseBytes", "freeBytes", "walBytes", "checkedAt"],
        ),
        "ServerCapabilities": object_schema(
            {
                "capabilities": array(text(min_length=1, max_length=100), max_items=500, unique=True),
                "minimumApiVersion": text(min_length=1, max_length=16),
                "minimumDesktopVersion": text(min_length=1, max_length=32),
            },
            ["capabilities", "minimumApiVersion", "minimumDesktopVersion"],
        ),
        "MaintenanceMode": object_schema(
            {
                "enabled": boolean(),
                "reason": text(max_length=2000, nullable=True),
                "enabledAt": timestamp(nullable=True),
                "version": integer(minimum=1, int_format="int64"),
            },
            ["enabled", "version"],
        ),
        "ScheduleConflict": object_schema(
            {
                "leftObjectId": identifier(),
                "rightObjectId": identifier(),
                "overlapStart": timestamp(),
                "overlapEnd": timestamp(),
                "severity": enum(["info", "warning", "blocking"]),
            },
            ["leftObjectId", "rightObjectId", "overlapStart", "overlapEnd", "severity"],
        ),
        "ScheduleItem": object_schema(
            {
                "objectId": identifier(),
                "itemType": enum(["task", "calendar_event"]),
                "title": text(min_length=1, max_length=500),
                "localDate": date(),
                "startAtUtc": timestamp(nullable=True),
                "endAtUtc": timestamp(nullable=True),
                "isAllDay": boolean(),
                "projectId": identifier(nullable=True),
                "status": text(min_length=1, max_length=20),
                "priority": enum(["low", "normal", "high", "critical"], nullable=True),
            },
            ["objectId", "itemType", "title", "localDate", "isAllDay", "status"],
        ),
        "SchedulePage": object_schema(
            {
                "items": array(reference("ScheduleItem"), max_items=500),
                "nextCursor": text(max_length=512, nullable=True),
                "rangeStart": timestamp(),
                "rangeEnd": timestamp(),
            },
            ["items", "rangeStart", "rangeEnd"],
        ),
        "SearchSuggestion": object_schema(
            {
                "object": reference("ObjectReference"),
                "matchedField": text(min_length=1, max_length=100),
                "highlight": text(max_length=1000),
                "score": {"type": "number", "format": "double", "minimum": 0},
            },
            ["object", "matchedField", "highlight", "score"],
        ),
        "SearchPage": object_schema(
            {
                "items": array(reference("SearchSuggestion"), max_items=500),
                "nextCursor": text(max_length=512, nullable=True),
                "tookMs": integer(minimum=0),
            },
            ["items", "tookMs"],
        ),
        "OccurrencePreview": object_schema(
            {
                "occurrenceKey": text(min_length=1, max_length=64),
                "localDate": date(),
                "startAtUtc": timestamp(nullable=True),
                "deadlineAt": timestamp(nullable=True),
                "dstAdjustment": enum(["none", "shifted_forward", "earlier_offset", "later_offset", "skipped"]),
            },
            ["occurrenceKey", "localDate", "dstAdjustment"],
        ),
        "CatalogTree": object_schema(
            {
                "rootItems": array(reference("CatalogTreeNode"), max_items=500),
                "nextCursor": text(max_length=512, nullable=True),
            },
            ["rootItems"],
        ),
        "CatalogTreeNode": object_schema(
            {
                "item": reference("CatalogItem"),
                "childIds": array(identifier(), max_items=1000),
            },
            ["item", "childIds"],
        ),
        "DepartmentTree": object_schema(
            {"departments": array(reference("Department"), max_items=1000)},
            ["departments"],
        ),
        "FeatureFlags": object_schema(
            {"items": array(reference("FeatureFlag"), max_items=500)},
            ["items"],
        ),
        "TagIds": object_schema(
            {"tagIds": array(identifier(), max_items=100, unique=True)},
            ["tagIds"],
        ),
        "NotificationPreferencesPatch": object_schema(
            {
                "enabled": boolean(),
                "desktopEnabled": boolean(),
                "soundEnabled": boolean(),
                "defaultSnoozeMinutes": integer(minimum=1, maximum=10080),
                "quietHoursStart": text(format_name="time", nullable=True),
                "quietHoursEnd": text(format_name="time", nullable=True),
                "quietHoursTimeZone": text(max_length=64, nullable=True),
            },
            min_properties=1,
        ),
        "OrganizationSettingsPatch": object_schema(
            {
                "trashRetentionDays": integer(minimum=1, maximum=3650),
                "historyRetentionDays": integer(minimum=90, maximum=36500),
                "changeFeedRetentionDays": integer(minimum=7, maximum=3650),
                "recurrenceHorizonDays": integer(minimum=7, maximum=730),
                "maxRequestBytes": integer(minimum=65536, maximum=10485760),
            },
            min_properties=1,
        ),
        "UserSettingsPatch": object_schema(
            {
                key: copy.deepcopy(value)
                for key, value in {
                    "language": text(min_length=2, max_length=16),
                    "timeFormat": enum(["12h", "24h"]),
                    "firstDayOfWeek": integer(minimum=1, maximum=7),
                    "workdayStart": text(format_name="time"),
                    "workdayEnd": text(format_name="time"),
                    "weekendDays": array(integer(minimum=1, maximum=7), min_items=1, max_items=6, unique=True),
                    "defaultTaskDurationMinutes": integer(minimum=5, maximum=1440),
                    "defaultReminderOffsetMinutes": integer(minimum=0, maximum=525600),
                    "autostartEnabled": boolean(),
                    "allowLocalPaths": boolean(),
                    "confirmCatalogDelete": boolean(),
                    "missingFileBehavior": enum(["show_actions", "keep_inactive", "prompt_relink"]),
                }.items()
            },
            min_properties=1,
        ),
    }


def sync_schemas() -> dict[str, dict[str, Any]]:
    change_record = object_schema(
        {
            "sequence": integer(minimum=1, int_format="int64"),
            "sourceEventId": identifier(),
            "objectType": text(min_length=1, max_length=40),
            "objectId": identifier(),
            "operation": enum(["upsert", "tombstone", "scope_revoke"]),
            "version": integer(minimum=1, int_format="int64"),
            "changedFields": array(text(max_length=200), max_items=500),
        },
        ["sequence", "sourceEventId", "objectType", "objectId", "operation", "version", "changedFields"],
    )
    snapshot_item = object_schema(
        {
            "ordinal": integer(minimum=1, int_format="int64"),
            "objectId": identifier(),
            "objectType": text(min_length=1, max_length=40),
            "objectVersion": integer(minimum=1, int_format="int64"),
            "payload": {
                "oneOf": [
                    reference(name)
                    for name in [
                        "User",
                        "Department",
                        "Project",
                        "ProjectMember",
                        "Task",
                        "Checklist",
                        "RecurrenceSeries",
                        "CalendarEvent",
                        "Reminder",
                        "Notification",
                        "Comment",
                        "Contact",
                        "Company",
                        "CatalogItem",
                        "NetworkResource",
                        "Tag",
                    ]
                ]
            },
        },
        ["ordinal", "objectId", "objectType", "objectVersion", "payload"],
    )
    snapshot_page = object_schema(
        {
            "mode": enum(["snapshot"]),
            "snapshotSessionId": identifier(),
            "cutSequence": integer(minimum=0, int_format="int64"),
            "scopeVersion": integer(minimum=1, int_format="int64"),
            "dataset": text(min_length=1, max_length=40),
            "items": array(reference("SnapshotItem"), max_items=500),
            "nextDataset": text(max_length=40, nullable=True),
            "nextOrdinal": integer(minimum=0, int_format="int64", nullable=True),
            "snapshotComplete": boolean(),
            "catchUpFromSequence": integer(minimum=0, int_format="int64"),
            "expiresAt": timestamp(),
        },
        [
            "mode",
            "snapshotSessionId",
            "cutSequence",
            "scopeVersion",
            "dataset",
            "items",
            "snapshotComplete",
            "catchUpFromSequence",
            "expiresAt",
        ],
    )
    incremental = object_schema(
        {
            "mode": enum(["incremental"]),
            "fromSequence": integer(minimum=0, int_format="int64"),
            "toSequence": integer(minimum=0, int_format="int64"),
            "scopeVersion": integer(minimum=1, int_format="int64"),
            "changes": array(reference("ChangeRecord"), max_items=500),
            "hasMore": boolean(),
            "nextCursor": text(max_length=512, nullable=True),
        },
        ["mode", "fromSequence", "toSequence", "scopeVersion", "changes", "hasMore"],
    )
    return {
        "ChangeRecord": change_record,
        "SnapshotItem": snapshot_item,
        "SnapshotPage": snapshot_page,
        "IncrementalSyncBatch": incremental,
        "SyncBatch": {
            "oneOf": [reference("SnapshotPage"), reference("IncrementalSyncBatch")],
            "discriminator": {
                "propertyName": "mode",
                "mapping": {
                    "snapshot": "#/components/schemas/SnapshotPage",
                    "incremental": "#/components/schemas/IncrementalSyncBatch",
                },
            },
        },
    }


def build_schemas() -> dict[str, dict[str, Any]]:
    schemas = {name: copy.deepcopy(schema) for name, schema in ENTITIES.items()}
    schemas.update(action_schemas())
    schemas.update(supplemental_schemas())
    schemas.update(sync_schemas())

    for schema_name, entity_schema in list(ENTITIES.items()):
        if schema_name in CREATE_REQUIRED:
            schemas[f"{schema_name}Create"] = object_schema(
                writable_properties(entity_schema),
                CREATE_REQUIRED[schema_name],
            )
        if schema_name not in {"BackgroundJobRun", "BackupRun"}:
            schemas[f"{schema_name}Patch"] = object_schema(
                writable_properties(entity_schema),
                min_properties=1,
                description=(
                    "PATCH semantics: omitted properties remain unchanged; an explicit null clears only nullable properties; "
                    "readOnly properties are rejected; at least one writable property is required."
                ),
            )

    schemas["FileLocation"]["properties"]["rawPath"] = text(
        min_length=1,
        max_length=4096,
        nullable=True,
        read_only=True,
    )
    schemas["FileLocation"]["properties"]["rawPath"]["x-redaction"] = (
        "null unless the caller is the owning device/user or has FileLocation.ReadSensitivePath"
    )
    schemas["FileLocation"]["properties"]["deviceAvailability"]["readOnly"] = True
    schemas["FileLocation"]["properties"]["redactedFields"] = array(
        enum(["rawPath"]),
        max_items=1,
        unique=True,
    )
    schemas["FileLocation"]["properties"]["redactedFields"]["readOnly"] = True
    schemas["FileLocation"]["required"].append("redactedFields")
    schemas["FileLocationCreate"]["properties"]["rawPath"]["writeOnly"] = True
    schemas["FileLocationPatch"]["properties"]["rawPath"]["writeOnly"] = True
    schemas["ProjectMemberPatch"]["properties"]["expectedProjectVersion"] = integer(
        minimum=1, int_format="int64"
    )
    schemas["FileLocationPatch"]["properties"]["expectedCatalogItemVersion"] = integer(
        minimum=1, int_format="int64"
    )
    schemas["RecurrenceSeriesCreate"]["properties"]["template"] = reference("RecurrenceTaskTemplate")
    schemas["RecurrenceSeriesCreate"]["required"] = list(
        dict.fromkeys([*schemas["RecurrenceSeriesCreate"]["required"], "template"])
    )
    schemas["RecurrenceSeriesPatch"]["properties"]["template"] = reference("RecurrenceTaskTemplate")

    page_bases = {
        "ArchiveEntry",
        "AuditEntry",
        "BackgroundJob",
        "BackgroundJobRun",
        "BackupRun",
        "CalendarEvent",
        "CatalogItem",
        "Checklist",
        "Comment",
        "CommentVersion",
        "Company",
        "Contact",
        "Department",
        "Device",
        "HistoryEntry",
        "InboxItem",
        "Interaction",
        "LoginAttempt",
        "NetworkResource",
        "Notification",
        "ObjectLink",
        "Project",
        "ProjectMember",
        "RecurrenceSeries",
        "Reminder",
        "Role",
        "Session",
        "Tag",
        "Task",
        "TrashEntry",
        "User",
    }
    for base in sorted(page_bases):
        schemas[f"{base}Page"] = object_schema(
            {
                "items": array(reference(base), max_items=500),
                "nextCursor": text(max_length=512, nullable=True),
                "total": integer(minimum=0, int_format="int64", nullable=True),
            },
            ["items"],
        )

    schemas["TodayItem"] = object_schema(
        {
            "objectId": identifier(),
            "itemType": enum(["task", "calendar_event", "reminder"]),
            "title": text(min_length=1, max_length=500),
            "localDate": date(),
            "startAtUtc": timestamp(nullable=True),
            "endAtUtc": timestamp(nullable=True),
            "isAllDay": boolean(),
            "projectId": identifier(nullable=True),
            "status": text(min_length=1, max_length=20),
            "priority": enum(["low", "normal", "high", "critical"], nullable=True),
            "recipientUserId": identifier(nullable=True),
        },
        ["objectId", "itemType", "title", "localDate", "isAllDay", "status"],
    )
    schemas["TodayPage"] = object_schema(
        {
            "date": date(),
            "timeZone": text(min_length=1, max_length=64),
            "items": array(reference("TodayItem"), max_items=500),
            "nextCursor": text(max_length=512, nullable=True),
        },
        ["date", "timeZone", "items"],
    )
    return schemas


QUERY_PARAMETERS: dict[str, dict[str, Any]] = {
    "page": integer(minimum=1, maximum=100000),
    "cursor": text(min_length=1, max_length=512),
    "limit": integer(minimum=1, maximum=500),
    "filter": text(max_length=2000),
    "sort": text(max_length=500),
    "status": text(max_length=40),
    "type": text(max_length=40),
    "objectType": text(max_length=40),
    "objectId": identifier(),
    "userId": identifier(),
    "deviceId": identifier(),
    "projectId": identifier(),
    "parentId": identifier(),
    "deletedBy": identifier(),
    "purgeBefore": timestamp(),
    "from": timestamp(),
    "to": timestamp(),
    "until": timestamp(),
    "q": text(min_length=2, max_length=200),
    "types": array(
        enum(
            [
                "task",
                "calendar_event",
                "project",
                "catalog_item",
                "file_location",
                "contact",
                "company",
                "interaction",
                "comment",
            ]
        ),
        min_items=1,
        max_items=9,
        unique=True,
    ),
    "projectIds": array(identifier(), max_items=100),
    "userIds": array(identifier(), max_items=100),
    "users": array(identifier(), max_items=100),
    "departments": array(identifier(), max_items=100),
    "contactIds": array(identifier(), min_items=1, max_items=100, unique=True),
    "hasFiles": boolean(),
    "lifecycle": array(
        enum(["active", "completed"]),
        min_items=1,
        max_items=2,
        unique=True,
    ),
    "projects": array(identifier(), max_items=100),
    "timezone": text(min_length=1, max_length=64),
    "scopeVersion": integer(minimum=1, int_format="int64"),
    "resource": text(max_length=60),
    "result": text(max_length=40),
    "includeArchived": boolean(),
    "depth": integer(minimum=1, maximum=10),
    "excludeObjectId": identifier(),
    "format": enum(["json", "csv"]),
    "actor": identifier(),
    "action": text(max_length=100),
    "outcome": enum(["success", "denied", "failure"]),
}

QUERY_PARAMETER_DESCRIPTIONS: dict[str, str] = {
    "types": "Canonical object types to search. Other filters are applied only to compatible types without client-side post-filtering.",
    "contactIds": "Return objects linked to at least one supplied contact identifier. Applied by the server before pagination.",
    "hasFiles": "When true, return only objects with at least one accessible file location; when false, only objects without accessible files.",
    "lifecycle": "Cross-type lifecycle filter. active excludes completed, archived and trashed objects; completed selects terminal business items but still excludes archived and trashed objects.",
    "cursor": "Opaque cursor bound to normalized filters, stable sort, authorization scope version and search-index snapshot. Reusing it with different filters is invalid.",
    "limit": "Maximum number of authorization-filtered results returned by the server.",
}


def parse_query(request_description: str) -> list[dict[str, Any]]:
    raw = request_description.removeprefix("query:").strip()
    names = [name.strip() for name in raw.split(",") if name.strip()]
    parameters = []
    for name in names:
        schema = copy.deepcopy(QUERY_PARAMETERS.get(name, text(max_length=500)))
        parameter: dict[str, Any] = {
            "name": name,
            "in": "query",
            "required": False,
            "schema": schema,
        }
        if name in QUERY_PARAMETER_DESCRIPTIONS:
            parameter["description"] = QUERY_PARAMETER_DESCRIPTIONS[name]
        if schema.get("type") == "array":
            parameter["style"] = "form"
            parameter["explode"] = True
        parameters.append(parameter)
    return parameters


def normalize_schema_name(value: str) -> str:
    page_match = re.fullmatch(r"Page<(.+)>", value)
    if page_match:
        return f"{page_match.group(1)}Page"
    return value.removesuffix("[]")


def response_schema(value: str) -> dict[str, Any] | None:
    if re.fullmatch(r"\d{3}", value):
        return None
    if value.endswith("[]"):
        return array(reference(value[:-2]), max_items=500)
    return reference(normalize_schema_name(value))


def request_schema(value: str) -> dict[str, Any] | None:
    if value == "—" or value.startswith("query:") or value.startswith("path:"):
        return None
    return reference(normalize_schema_name(value))


def operation_id(method: str, path: str) -> str:
    segments = re.sub(r"[^A-Za-z0-9]+", "_", f"{method}_{path}").strip("_")
    return segments


def response_description(code: int) -> str:
    return {
        200: "Successful response.",
        201: "Resource created.",
        202: "Command accepted for asynchronous execution.",
        204: "Command completed without a response body.",
        400: "Malformed request or invalid query syntax.",
        401: "Authentication failed or token is no longer valid.",
        403: "Authenticated principal lacks the required permission or relationship.",
        404: "Resource is absent or hidden by authorization scope.",
        409: "Domain conflict, idempotency-key collision, or secondary-version conflict.",
        410: "Sync cursor or retained resource has expired.",
        412: "If-Match does not match the current aggregate version.",
        413: "Request exceeds the configured size limit.",
        422: "Syntactically valid request violates field or domain invariants.",
        423: "Account or resource is locked.",
        428: "If-Match is required for this operation.",
        429: "Rate limit exceeded.",
        500: "Unexpected server failure.",
        503: "Required dependency is unavailable.",
    }.get(code, "Response.")


def load_error_codes_by_http() -> dict[int, list[str]]:
    with ERROR_CATALOG.open(encoding="utf-8-sig", newline="") as source:
        rows = list(csv.DictReader(source))
    result: dict[int, list[str]] = {}
    for row in rows:
        result.setdefault(int(row["http"]), []).append(row["code"])
    return {code: sorted(values) for code, values in result.items()}


def update_api_catalog() -> list[dict[str, str]]:
    with API_CATALOG.open(encoding="utf-8-sig", newline="") as source:
        rows = list(csv.DictReader(source))
        fieldnames = list(rows[0].keys())

    replacements = {
        "/api/v1/roles/{id}/restore": ("/api/v1/roles/{id}/activate", "Активировать неактивную роль"),
        "/api/v1/recurrence-series/{id}/restore": (
            "/api/v1/recurrence-series/{id}/resume",
            "Возобновить приостановленную серию",
        ),
        "/api/v1/reminders/{id}/restore": (
            "/api/v1/reminders/{id}/reschedule",
            "Перепланировать отменённое напоминание",
        ),
    }
    for row in rows:
        row["effects"] = row["effects"].replace("change-feed", "domain-event")
        if row["path"] in replacements:
            row["path"], row["purpose"] = replacements[row["path"]]
        if row["path"] == "/api/v1/roles/{id}" and row["method"] == "DELETE":
            row["purpose"] = "Деактивировать пользовательскую роль"
            row["response"] = "Role"
            row["events"] = "RoleDeactivated"
        if row["path"] == "/api/v1/recurrence-series/{id}" and row["method"] == "DELETE":
            row["purpose"] = "Отменить серию без помещения в универсальную корзину"
            row["response"] = "RecurrenceSeries"
            row["events"] = "RecurrenceSeriesCancelled"
        if row["path"] == "/api/v1/reminders/{id}" and row["method"] == "DELETE":
            row["purpose"] = "Отменить напоминание"
            row["response"] = "Reminder"
            row["events"] = "ReminderCancelled"
        if row["path"] == "/api/v1/archive/{objectId}/restore":
            row["permission"] = "Archive.Restore"
        if row["path"] == "/api/v1/trash/{objectId}/restore":
            row["permission"] = "Trash.Restore"
        if row["path"] == "/api/v1/auth/refresh":
            row["permission"] = "Anonymous.SessionRefresh"
        if row["path"] in {
            "/api/v1/catalog-items/{id}/locations",
            "/api/v1/catalog-items/{id}/resolve-location",
        } and row["method"] in {"GET", "POST"} and "FileLocation.ReadSensitivePath" not in row["effects"]:
            row["effects"] = (
                (row["effects"] + "; ") if row["effects"] != "—" else ""
            ) + "rawPath only for owning device/user or FileLocation.ReadSensitivePath"

        if "If-Match" in row["locking"] and "optional" not in row["locking"]:
            codes = {int(code) for code in row["codes"].split(",") if code}
            codes.update({409, 412, 428})
            row["codes"] = ",".join(str(code) for code in sorted(codes))
        if row["effects"] != "—":
            row["effects"] = "; ".join(
                dict.fromkeys(
                    part.strip() for part in row["effects"].split(";") if part.strip()
                )
            )

    if not any(row["path"] == "/api/v1/today" for row in rows):
        rows.append(
            {
                "module": "calendar",
                "method": "GET",
                "path": "/api/v1/today",
                "purpose": "Агрегированный read-model «Сегодня» в часовом поясе пользователя",
                "permission": "Calendar.Read",
                "request": "query:timezone,cursor,limit",
                "response": "TodayPage",
                "codes": "200,400,401,403,422",
                "idempotency": "Safe",
                "transaction": "Read-only repeatable snapshot",
                "locking": "—",
                "effects": "—",
                "events": "—",
            }
        )

    rows.sort(key=lambda row: (row["path"], row["method"]))
    with API_CATALOG.open("w", encoding="utf-8", newline="") as target:
        writer = csv.DictWriter(target, fieldnames=fieldnames, lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)
    return rows


def build_openapi(rows: list[dict[str, str]], schemas: dict[str, dict[str, Any]]) -> dict[str, Any]:
    error_codes_by_http = load_error_codes_by_http()
    document: dict[str, Any] = {
        "openapi": "3.1.0",
        "jsonSchemaDialect": "https://json-schema.org/draft/2020-12/schema",
        "info": {
            "title": "Organizer Local Server API",
            "version": "1.2.0-stage2.2",
            "license": {
                "name": "Proprietary",
                "url": "https://organizer.company.local/legal/license",
            },
            "description": (
                "Normative Stage 2.2 API recovered from the validated Stage 2.1 contract. "
                "If-Match guards the aggregate named by "
                "x-if-match-target. A missing header returns 428, a stale ETag returns "
                "412, and domain or secondary-version conflicts return 409. "
                "Idempotency-Key is scoped by organization, user and operation; the "
                "same key with a different request hash returns 409."
            ),
        },
        "servers": [{"url": "https://organizer.company.local"}],
        "tags": [
            {"name": tag, "description": f"{tag} module operations."}
            for tag in sorted({row["module"] for row in rows})
        ],
        "paths": {},
        "components": {
            "securitySchemes": {
                "BearerAuth": {
                    "type": "http",
                    "scheme": "bearer",
                    "bearerFormat": "JWT",
                }
            },
            "parameters": {
                "IdempotencyKey": {
                    "name": "Idempotency-Key",
                    "in": "header",
                    "required": True,
                    "description": "Opaque 8-200 character key. Stored with the SHA-256 request hash.",
                    "schema": text(min_length=8, max_length=200, pattern=r"^[\x21-\x7E]+$"),
                },
                "OptionalIdempotencyKey": {
                    "name": "Idempotency-Key",
                    "in": "header",
                    "required": False,
                    "description": "Optional replay key for naturally single-use or anonymous operations.",
                    "schema": text(min_length=8, max_length=200, pattern=r"^[\x21-\x7E]+$"),
                },
                "IfMatch": {
                    "name": "If-Match",
                    "in": "header",
                    "required": True,
                    "description": 'Strong ETag in the form "v<positive-int64>".',
                    "schema": text(pattern=r'^"v[1-9][0-9]*"$', max_length=64),
                },
                "OptionalIfMatch": {
                    "name": "If-Match",
                    "in": "header",
                    "required": False,
                    "description": "Optional strong ETag used only when a target resource already exists.",
                    "schema": text(pattern=r'^"v[1-9][0-9]*"$', max_length=64),
                },
                "CorrelationId": {
                    "name": "X-Correlation-ID",
                    "in": "header",
                    "required": False,
                    "schema": identifier(),
                },
            },
            "headers": {
                "ETag": {
                    "description": "Strong current aggregate version.",
                    "schema": text(pattern=r'^"v[1-9][0-9]*"$'),
                },
                "CorrelationId": {
                    "description": "Correlation identifier used in audit and domain events.",
                    "schema": identifier(),
                },
                "IdempotencyReplayed": {
                    "description": "True when the stored response was replayed.",
                    "schema": boolean(),
                },
            },
            "schemas": schemas,
        },
        "security": [{"BearerAuth": []}],
    }

    for row in rows:
        path = row["path"]
        method = row["method"].lower()
        operation: dict[str, Any] = {
            "tags": [row["module"]],
            "operationId": operation_id(row["method"], path),
            "summary": row["purpose"],
            "x-permission": row["permission"],
            "x-transaction": row["transaction"],
            "x-optimistic-lock": row["locking"],
            "x-idempotency": row["idempotency"],
            "x-side-effects": row["effects"],
            "x-domain-events": [] if row["events"] == "—" else [event.strip() for event in row["events"].split(",")],
            "x-required-capability": row["permission"],
            "parameters": [{"$ref": "#/components/parameters/CorrelationId"}],
            "responses": {},
        }
        if row["permission"] in {
            "Authenticated",
            "Anonymous",
            "Anonymous.SessionRefresh",
            "Anonymous/Network allowlist",
        }:
            operation["x-access-policy"] = row["permission"]
            operation.pop("x-permission")
            operation.pop("x-required-capability")

        if path == "/api/v1/auth/refresh" or row["permission"].startswith("Anonymous"):
            operation["security"] = []

        for parameter_name in re.findall(r"{([^}]+)}", path):
            parameter_schema = (
                integer(minimum=1, int_format="int64")
                if parameter_name == "version"
                else text(min_length=1, max_length=64)
                if parameter_name == "occurrenceKey"
                else identifier()
            )
            operation["parameters"].append(
                {
                    "name": parameter_name,
                    "in": "path",
                    "required": True,
                    "schema": parameter_schema,
                }
            )

        if row["request"].startswith("query:"):
            query_parameters = parse_query(row["request"])
            operation["parameters"].extend(query_parameters)
            query_names = [parameter["name"] for parameter in query_parameters]
            operation["x-filters"] = [
                name for name in query_names if name not in {"cursor", "page", "limit", "sort"}
            ]
            if "sort" in query_names:
                operation["x-server-side-sorting"] = True
            if "cursor" in query_names:
                operation["x-pagination"] = {
                    "style": "cursor",
                    "cursorParameter": "cursor",
                    "limitParameter": "limit" if "limit" in query_names else None,
                }
            elif "page" in query_names:
                operation["x-pagination"] = {
                    "style": "page",
                    "pageParameter": "page",
                }
        if path == "/api/v1/search" and method == "get":
            operation["x-server-side-filtering"] = True
            operation["x-client-post-filtering"] = "forbidden"
            operation["x-cursor-pagination"] = {
                "stableSort": ["relevance desc", "updatedAt desc", "type asc", "id asc"],
                "boundTo": [
                    "normalized query",
                    "types",
                    "projectIds",
                    "userIds",
                    "departments",
                    "contactIds",
                    "hasFiles",
                    "lifecycle",
                    "from",
                    "to",
                    "authorization scope version",
                    "search index snapshot",
                ],
                "invalidCursorError": "SEARCH_CURSOR_INVALID",
                "expiredCursorError": "SEARCH_CURSOR_EXPIRED",
            }
            operation["x-filter-compatibility"] = {
                "onIncompatibleType": (
                    "exclude that type from the server result set before pagination; "
                    "return 422 only when no requested type supports a supplied filter"
                ),
                "contactIds": [
                    "task",
                    "calendar_event",
                    "project",
                    "catalog_item",
                    "contact",
                    "company",
                    "interaction",
                    "comment",
                ],
                "hasFiles": [
                    "task",
                    "calendar_event",
                    "project",
                    "catalog_item",
                    "contact",
                    "company",
                    "interaction",
                    "comment",
                ],
                "lifecycle": [
                    "task",
                    "calendar_event",
                    "project",
                    "catalog_item",
                    "contact",
                    "company",
                    "interaction",
                    "comment",
                ],
            }

        idempotency_declared = row["idempotency"].startswith("Idempotency-Key")
        idempotency_required = idempotency_declared and "optional" not in row["idempotency"].lower()
        if idempotency_required:
            operation["parameters"].append({"$ref": "#/components/parameters/IdempotencyKey"})
        elif idempotency_declared:
            operation["parameters"].append({"$ref": "#/components/parameters/OptionalIdempotencyKey"})

        if_match_required = "If-Match" in row["locking"] and "optional" not in row["locking"] and "Per-item" not in row["locking"]
        if if_match_required:
            operation["parameters"].append({"$ref": "#/components/parameters/IfMatch"})
            target = row["locking"].replace("If-Match", "").replace("required", "").strip(" /-")
            operation["x-if-match-target"] = target or "resource identified by the request path"
        elif "If-Match optional" in row["locking"]:
            operation["parameters"].append({"$ref": "#/components/parameters/OptionalIfMatch"})

        if "FileLocation.ReadSensitivePath" in row["effects"]:
            operation["x-sensitive-field-permission"] = "FileLocation.ReadSensitivePath"

        body_schema = request_schema(row["request"])
        if body_schema is not None:
            operation["requestBody"] = {
                "required": True,
                "content": {
                    "application/json": {
                        "schema": body_schema,
                    }
                },
            }

        codes = {int(code) for code in row["codes"].split(",") if code}
        if if_match_required:
            codes.update({409, 412, 428})
        if not any(400 <= code < 500 for code in codes):
            codes.add(400)
        operation["x-error-codes"] = sorted(
            {
                error_code
                for code in codes
                if code >= 400
                for error_code in error_codes_by_http.get(code, [])
            }
        )
        for code in sorted(codes):
            response: dict[str, Any] = {"description": response_description(code)}
            if code < 300:
                response["headers"] = {
                    "X-Correlation-ID": {"$ref": "#/components/headers/CorrelationId"}
                }
                if code != 204:
                    response["headers"]["ETag"] = {"$ref": "#/components/headers/ETag"}
                if idempotency_declared:
                    response["headers"]["Idempotency-Replayed"] = {
                        "$ref": "#/components/headers/IdempotencyReplayed"
                    }
                schema = response_schema(row["response"])
                if schema is not None and code != 204:
                    response["content"] = {
                        "application/json": {
                            "schema": schema,
                        }
                    }
            else:
                response["content"] = {
                    "application/problem+json": {
                        "schema": reference("ProblemDetails"),
                    }
                }
            operation["responses"][str(code)] = response

        if not operation["parameters"]:
            operation.pop("parameters")
        document["paths"].setdefault(path, {})[method] = operation

    return document


def referenced_schema_names(document: dict[str, Any]) -> set[str]:
    references: set[str] = set()

    def visit(value: Any) -> None:
        if isinstance(value, dict):
            reference_value = value.get("$ref")
            if isinstance(reference_value, str) and reference_value.startswith("#/components/schemas/"):
                references.add(reference_value.rsplit("/", 1)[1])
            for child in value.values():
                visit(child)
        elif isinstance(value, list):
            for child in value:
                visit(child)

    visit(document)
    return references


def prune_unused_schemas(document: dict[str, Any]) -> None:
    schemas = document["components"]["schemas"]
    reachable: set[str] = set()
    pending = list(referenced_schema_names({"paths": document["paths"]}))
    while pending:
        name = pending.pop()
        if name in reachable or name not in schemas:
            continue
        reachable.add(name)
        pending.extend(referenced_schema_names(schemas[name]) - reachable)
    document["components"]["schemas"] = {
        name: schema for name, schema in schemas.items() if name in reachable
    }


rows = update_api_catalog()
schemas = build_schemas()
document = build_openapi(rows, schemas)
prune_unused_schemas(document)
schemas = document["components"]["schemas"]
missing = sorted(referenced_schema_names(document) - schemas.keys())
if missing:
    raise RuntimeError(f"Missing concrete schemas: {', '.join(missing)}")

OPENAPI.write_text(
    yaml.dump(
        document,
        Dumper=NoAliasDumper,
        sort_keys=False,
        allow_unicode=True,
        width=120,
    ),
    encoding="utf-8",
    newline="\n",
)
