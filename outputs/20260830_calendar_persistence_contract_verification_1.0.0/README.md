# Calendar persistence contract verification 1.0.0

Пакет фиксирует повторную фактическую проверку calendar persistence contract на свежем изолированном PostgreSQL 16 до начала HTTP/UI слоя.

Проверены схема миграций, CalendarEvent round-trip, tenant isolation, optimistic concurrency, порядок участников, lifecycle transitions и unified schedule read model. Production HTTP endpoints и Desktop UI в этот пакет не входят.
