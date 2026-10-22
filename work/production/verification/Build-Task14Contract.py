"""Build the Task 14 runtime contract from the immutable Stage 2.3 baseline.

Requires PyYAML 6.0.3; sample validation requires jsonschema 4.25.1 and
rfc3339-validator 0.1.4. No network access or source modifications occur here.
"""
import argparse
import copy
import csv
import hashlib
import json
from pathlib import Path
import zipfile

import yaml

ROOT = Path(__file__).resolve().parents[3]
BASE_SHA = '36c15dff5adba0041fcfd79f5a0d203835dac5cdd4ad24122bcd92177c13220d'
ERRORS = {
    'AUTHENTICATION_REQUIRED': [401], 'SESSION_EXPIRED': [401], 'SESSION_REVOKED': [401],
    'DEVICE_REVOKED': [401], 'ACCOUNT_BLOCKED': [423], 'FORBIDDEN': [403],
    'OBJECT_NOT_VISIBLE': [404], 'OBJECT_ARCHIVED': [409], 'OBJECT_DELETED': [409],
    'INVALID_STATE_TRANSITION': [409], 'IDEMPOTENCY_KEY_REUSED': [409],
    'IDEMPOTENCY_REQUEST_IN_PROGRESS': [409], 'PRECONDITION_REQUIRED': [428],
    'VERSION_CONFLICT': [412], 'VALIDATION_FAILED': [400, 422], 'MALFORMED_JSON': [400],
    'INTERNAL_ERROR': [500, 503],
}


def build(output, samples):
    if not output.is_relative_to(ROOT) or output.relative_to(ROOT).parts[0] not in ('work', 'outputs'):
        raise ValueError('Generated contracts must remain within work/ or outputs/ of this repository.')
    source = ROOT / 'sources/stage_2_3/Organizer_Stage2_Technical_Specification_2.3_Final.zip'
    with zipfile.ZipFile(source) as archive:
        raw = archive.read('stage_2_3/openapi/openapi.yaml')
    if hashlib.sha256(raw).hexdigest() != BASE_SHA:
        raise ValueError('The pinned normative OpenAPI changed; review the amendment before rebuilding.')
    api = yaml.safe_load(raw)
    api['info']['version'] += '+task14.4.1'
    api['x-task14-amendment'] = {
        'version': '1.0.0', 'baseOpenApiSha256': BASE_SHA,
        'scope': ['HistoryEntry', 'ProblemDetails.retryable', 'task transition errors', 'task/project history pagination'],
        'decision': 'work/production/docs/task-14-final-contract-corrections.md',
    }
    schemas = api['components']['schemas']
    entry = schemas['HistoryEntry']
    canonical_required = copy.deepcopy(entry['required'])
    entry['properties']['changeType']['enum'] = [
        'created', 'updated', 'state_changed', 'archived', 'restored', 'trashed', 'purged']
    entry['properties']['changedAt']['pattern'] = 'Z$'
    entry['properties'].update({
        'action': {'type': 'string', 'minLength': 1, 'description': 'Original domain event type; retained for desktop compatibility.'},
        'version': {'type': 'integer', 'minimum': 1, 'deprecated': True, 'description': 'Legacy alias of objectVersion.'},
        'occurredAt': {'type': 'string', 'format': 'date-time', 'deprecated': True, 'description': 'Legacy alias of changedAt.'},
        'actorUserId': {'type': ['string', 'null'], 'format': 'uuid', 'description': 'Event actor; null for system events.'},
    })
    assert entry['required'] == canonical_required and entry['additionalProperties'] is False
    schemas['ProblemDetails']['properties']['retryable'] = {'type': 'boolean'}

    transition = api['paths']['/api/v1/tasks/{id}/transition']['post']
    transition['x-error-codes'] = sorted(ERRORS)
    transition['x-error-statuses'] = ERRORS
    for status, description in [(423, 'The account is blocked.'),
                                (500, 'Unexpected internal command result.'),
                                (503, 'Task write storage is temporarily unavailable.')]:
        response = copy.deepcopy(transition['responses']['400'])
        response['description'] = description
        transition['responses'][str(status)] = response
    transition['responses']['409'].setdefault('headers', {})['Retry-After'] = {
        'description': 'Present for IDEMPOTENCY_REQUEST_IN_PROGRESS; seconds before retrying the original request.',
        'schema': {'type': 'integer', 'minimum': 1},
    }
    # Existing runtime history endpoints use stable page/limit, not an opaque cursor.
    for resource in ('tasks', 'projects'):
        operation = api['paths'][f'/api/v1/{resource}/{{id}}/history']['get']
        operation['parameters'] = [p for p in operation['parameters'] if p.get('name') != 'cursor']
        for parameter in operation['parameters']:
            if parameter.get('name') == 'page':
                parameter['schema']['maximum'] = 10000
        operation['parameters'].append({'name': 'limit', 'in': 'query', 'required': False,
            'schema': {'type': 'integer', 'minimum': 1, 'maximum': 200, 'default': 50}})
        operation['x-pagination'] = {'style': 'page', 'pageParameter': 'page', 'limitParameter': 'limit'}
        operation['responses']['200'].get('headers', {}).pop('ETag', None)

    output.mkdir(parents=True, exist_ok=True)
    (output / 'openapi.json').write_text(json.dumps(api, ensure_ascii=False, indent=2, default=str) + '\n', encoding='utf-8')
    with (output / 'task-transition-errors.csv').open('w', encoding='utf-8', newline='') as stream:
        writer = csv.writer(stream, lineterminator='\n')
        writer.writerow(['operation', 'code', 'http'])
        for code, statuses in sorted(ERRORS.items()):
            for status in statuses:
                writer.writerow(['POST_api_v1_tasks_id_transition', code, status])

    validation = {'baseOpenApiSha256': BASE_SHA, 'canonicalRequiredFieldsPreserved': canonical_required,
                  'legacyFieldsDeclared': ['action', 'version', 'occurredAt', 'actorUserId'], 'samples': []}
    if samples is not None:
        from jsonschema import Draft202012Validator, FormatChecker
        validator = Draft202012Validator({'$ref': '#/components/schemas/HistoryEntryPage', 'components': api['components']},
                                         format_checker=FormatChecker())
        problem = Draft202012Validator({'$ref': '#/components/schemas/ProblemDetails', 'components': api['components']},
                                       format_checker=FormatChecker())
        files = sorted(samples.glob('*.json'))
        if not files:
            raise ValueError('No actual runtime samples were supplied.')
        history_count = error_count = 0
        for path in files:
            instance = json.loads(path.read_text(encoding='utf-8-sig'))
            if path.name.startswith('transition-error-'):
                problem.validate(instance['body'])
                assert instance['status'] == instance['body']['status']
                assert instance['status'] in ERRORS[instance['body']['code']]
                if instance['body']['code'] == 'IDEMPOTENCY_REQUEST_IN_PROGRESS':
                    assert instance['retryAfterSeconds'] >= 1
                error_count += 1
            else:
                validator.validate(instance)
                # A missing mandatory field must be rejected, including correlation/version.
                for field in canonical_required:
                    invalid = copy.deepcopy(instance)
                    del invalid['items'][0][field]
                    assert not validator.is_valid(invalid), field
                history_count += 1
            validation['samples'].append(path.name)
        assert history_count >= 10 and error_count >= 6, 'Incomplete history/error scenario evidence.'
        validation.update({'historySamples': history_count, 'errorSamples': error_count,
                           'missingRequiredFieldNegativeChecks': history_count * len(canonical_required), 'passed': True})
    (output / 'contract-validation.json').write_text(json.dumps(validation, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(validation, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--samples', type=Path)
    args = parser.parse_args()
    build(args.out.resolve(), args.samples.resolve() if args.samples else None)
