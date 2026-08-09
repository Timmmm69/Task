from __future__ import annotations

import os
import threading
import time
from dataclasses import dataclass
from typing import Callable

import psycopg


DSN = os.environ.get(
    "ORGANIZER_TEST_DSN",
    "host=127.0.0.1 port=55439 dbname=organizer_stage_2_1 "
    "user=organizer_migrator password=organizer_local_validation_only",
)
ORGANIZATION_ID = "01900000-0000-7000-8000-000000000001"
USER_ID = "01900000-0000-7000-8000-000000000003"


@dataclass
class RaceResult:
    name: str
    outcome: str
    sqlstate: str | None
    elapsed_seconds: float


def run_race(
    left_action: Callable[[psycopg.Connection], None],
    right_action: Callable[[psycopg.Connection], None],
) -> list[RaceResult]:
    barrier = threading.Barrier(2)
    results: list[RaceResult] = []
    result_lock = threading.Lock()

    def execute(name: str, action: Callable[[psycopg.Connection], None]) -> None:
        started = time.monotonic()
        try:
            with psycopg.connect(DSN, autocommit=False) as connection:
                with connection.cursor() as cursor:
                    cursor.execute("SET LOCAL lock_timeout = '5s'")
                    cursor.execute("SET LOCAL statement_timeout = '10s'")
                barrier.wait(timeout=5)
                action(connection)
                connection.commit()
            result = RaceResult(name, "committed", None, time.monotonic() - started)
        except psycopg.Error as error:
            result = RaceResult(
                name,
                "rejected",
                error.sqlstate,
                time.monotonic() - started,
            )
        with result_lock:
            results.append(result)

    threads = [
        threading.Thread(target=execute, args=("left", left_action), daemon=True),
        threading.Thread(target=execute, args=("right", right_action), daemon=True),
    ]
    for thread in threads:
        thread.start()
    for thread in threads:
        thread.join(timeout=15)
        if thread.is_alive():
            raise AssertionError("Concurrent operation did not terminate")
    return results


def assert_safe_conflict(name: str, results: list[RaceResult]) -> None:
    outcomes = sorted(result.outcome for result in results)
    sqlstates = {result.sqlstate for result in results if result.sqlstate}
    if outcomes != ["committed", "rejected"]:
        raise AssertionError(f"{name}: expected one commit and one rejection, got {results}")
    if sqlstates != {"23514"}:
        raise AssertionError(f"{name}: expected invariant rejection 23514, got {results}")
    if any(result.elapsed_seconds >= 10 for result in results):
        raise AssertionError(f"{name}: exceeded bounded completion time: {results}")


def setup() -> None:
    task_ids = [
        "01900000-0000-7000-8000-000000000101",
        "01900000-0000-7000-8000-000000000102",
        "01900000-0000-7000-8000-000000000103",
        "01900000-0000-7000-8000-000000000104",
    ]
    catalog_ids = [
        "01900000-0000-7000-8000-000000000111",
        "01900000-0000-7000-8000-000000000112",
    ]
    with psycopg.connect(DSN) as connection:
        with connection.cursor() as cursor:
            for object_id in task_ids:
                cursor.execute(
                    """
                    INSERT INTO core.objects (
                        id, organization_id, object_type, created_by, updated_by
                    )
                    VALUES (%s, %s, 'task', %s, %s)
                    ON CONFLICT (id) DO NOTHING
                    """,
                    (object_id, ORGANIZATION_ID, USER_ID, USER_ID),
                )
                cursor.execute(
                    """
                    INSERT INTO work.tasks (
                        id, organization_id, title, author_user_id, creator_user_id
                    )
                    VALUES (%s, %s, %s, %s, %s)
                    ON CONFLICT (id) DO NOTHING
                    """,
                    (object_id, ORGANIZATION_ID, f"Race {object_id[-3:]}", USER_ID, USER_ID),
                )
            for object_id in catalog_ids:
                cursor.execute(
                    """
                    INSERT INTO core.objects (
                        id, organization_id, object_type, created_by, updated_by
                    )
                    VALUES (%s, %s, 'catalog_item', %s, %s)
                    ON CONFLICT (id) DO NOTHING
                    """,
                    (object_id, ORGANIZATION_ID, USER_ID, USER_ID),
                )
                cursor.execute(
                    """
                    INSERT INTO files.catalog_items (
                        id, organization_id, item_type, name, created_by
                    )
                    VALUES (%s, %s, 'virtual_folder', %s, %s)
                    ON CONFLICT (id) DO NOTHING
                    """,
                    (object_id, ORGANIZATION_ID, f"Folder {object_id[-3:]}", USER_ID),
                )


def update_task_parent(task_id: str, parent_id: str) -> Callable[[psycopg.Connection], None]:
    def action(connection: psycopg.Connection) -> None:
        with connection.cursor() as cursor:
            cursor.execute(
                "UPDATE work.tasks SET parent_task_id = %s WHERE id = %s",
                (parent_id, task_id),
            )

    return action


def update_catalog_parent(item_id: str, parent_id: str) -> Callable[[psycopg.Connection], None]:
    def action(connection: psycopg.Connection) -> None:
        with connection.cursor() as cursor:
            cursor.execute(
                "UPDATE files.catalog_items SET parent_item_id = %s WHERE id = %s",
                (parent_id, item_id),
            )

    return action


def add_dependency(predecessor_id: str, successor_id: str) -> Callable[[psycopg.Connection], None]:
    def action(connection: psycopg.Connection) -> None:
        with connection.cursor() as cursor:
            cursor.execute(
                """
                INSERT INTO work.task_dependencies (
                    predecessor_task_id, successor_task_id, created_by
                )
                VALUES (%s, %s, %s)
                """,
                (predecessor_id, successor_id, USER_ID),
            )

    return action


setup()

task_parent_results = run_race(
    update_task_parent(
        "01900000-0000-7000-8000-000000000101",
        "01900000-0000-7000-8000-000000000102",
    ),
    update_task_parent(
        "01900000-0000-7000-8000-000000000102",
        "01900000-0000-7000-8000-000000000101",
    ),
)
assert_safe_conflict("task parent race", task_parent_results)

catalog_parent_results = run_race(
    update_catalog_parent(
        "01900000-0000-7000-8000-000000000111",
        "01900000-0000-7000-8000-000000000112",
    ),
    update_catalog_parent(
        "01900000-0000-7000-8000-000000000112",
        "01900000-0000-7000-8000-000000000111",
    ),
)
assert_safe_conflict("catalog parent race", catalog_parent_results)

dependency_results = run_race(
    add_dependency(
        "01900000-0000-7000-8000-000000000103",
        "01900000-0000-7000-8000-000000000104",
    ),
    add_dependency(
        "01900000-0000-7000-8000-000000000104",
        "01900000-0000-7000-8000-000000000103",
    ),
)
assert_safe_conflict("task dependency race", dependency_results)

print("CONCURRENCY_TESTS_PASSED")
for result in [*task_parent_results, *catalog_parent_results, *dependency_results]:
    print(
        f"{result.name}: {result.outcome} sqlstate={result.sqlstate or '-'} "
        f"elapsed={result.elapsed_seconds:.3f}s"
    )
