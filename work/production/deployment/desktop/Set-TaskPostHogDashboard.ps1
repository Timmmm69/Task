param(
    [ValidateSet('https://eu.posthog.com', 'https://us.posthog.com')]
    [string]$PostHogHost,
    [ValidateRange(1, [int]::MaxValue)]
    [int]$ProjectId,
    [switch]$DefinitionsOnly
)

$ErrorActionPreference = 'Stop'

$dashboardName = 'Task — Product Usage'
$dashboardDescription = 'Запуски, полезные действия и возвращаемость установок Task с разрезом Personal / Corporate.'
$eventNames = "'app_opened', 'task_created', 'task_updated', 'task_completed', 'task_planned', 'calendar_event_created', 'calendar_event_updated', 'project_created', 'inbox_item_captured'"

$taskEvents = @"
SELECT uuid, event, distinct_id AS installation, timestamp,
       toDate(toTimeZone(timestamp, 'UTC')) AS day,
       arrayJoin(['all', toString(properties.mode)]) AS segment
FROM events
WHERE toInt(properties.task_analytics_schema) = 1
  AND toString(properties.task_analytics_test) IN ('false', '0')
  AND toString(properties.mode) IN ('personal', 'corporate')
  AND event IN ($eventNames)
  AND {filters}
"@

function New-Insight($Name, $Description, $Sql, $Display = 'ActionsTable', $ChartSettings = $null) {
    $query = @{
        kind = 'DataVisualizationNode'
        display = $Display
        source = @{ kind = 'HogQLQuery'; query = $Sql }
    }
    if ($ChartSettings) { $query.chartSettings = $ChartSettings }
    @{ name = $Name; description = $Description; query = $query }
}

$insights = @(
    New-Insight 'Активные установки · DAU / WAU / MAU' 'Уникальные установки с полезными действиями за текущий день и окна 7 и 30 дней (UTC).' @"
WITH task_events AS ($taskEvents)
SELECT segment,
    uniqExactIf(installation, day = toDate(toTimeZone(now(), 'UTC'))) AS DAU,
    uniqExactIf(installation, day >= toDate(toTimeZone(now(), 'UTC')) - 6) AS WAU,
    uniqExact(installation) AS MAU
FROM task_events
WHERE event != 'app_opened' AND day BETWEEN toDate(toTimeZone(now(), 'UTC')) - 29 AND toDate(toTimeZone(now(), 'UTC'))
GROUP BY segment ORDER BY segment LIMIT 3
"@
    New-Insight 'Новые и вернувшиеся установки' 'По дням UTC. Новые впервые выполнили полезное действие в этом режиме, вернувшиеся уже делали это ранее.' @"
WITH task_events AS ($taskEvents),
first_days AS (
    SELECT segment, installation, min(day) AS first_day FROM task_events
    WHERE event != 'app_opened' GROUP BY segment, installation
)
SELECT e.day AS activity_day, e.segment AS segment,
    uniqExactIf(e.installation, e.day = f.first_day) AS new_installations,
    uniqExactIf(e.installation, e.day > f.first_day) AS returning_installations
FROM task_events e INNER JOIN first_days f ON e.segment = f.segment AND e.installation = f.installation
WHERE e.event != 'app_opened' AND e.day BETWEEN toDate(toTimeZone(now(), 'UTC')) - 29 AND toDate(toTimeZone(now(), 'UTC'))
GROUP BY activity_day, segment ORDER BY activity_day DESC, segment LIMIT 90
"@
    New-Insight 'Полезные действия по функциям' 'Подтверждённые действия за 30 календарных дней UTC. Повтор запроса с тем же UUID учитывается один раз.' @"
WITH task_events AS ($taskEvents)
SELECT event AS feature, segment, uniqExact(uuid) AS successful_actions
FROM task_events
WHERE event != 'app_opened' AND day BETWEEN toDate(toTimeZone(now(), 'UTC')) - 29 AND toDate(toTimeZone(now(), 'UTC'))
GROUP BY feature, segment ORDER BY successful_actions DESC, feature, segment LIMIT 30
"@ 'ActionsBarValue' @{ xAxis = @{ column = 'feature' }; yAxis = @(@{ column = 'successful_actions' }); seriesBreakdownColumn = 'segment' }
    New-Insight 'Воронка · запуск → полезное действие' 'Установки, открывшие Task за 30 календарных дней UTC. Конверсия требует подтверждённого действия в тот же момент или позже.' @"
WITH task_events AS ($taskEvents),
installs AS (
    SELECT segment, installation, countIf(event = 'app_opened') AS opens,
        minIf(timestamp, event = 'app_opened') AS first_open,
        maxIf(timestamp, event != 'app_opened') AS last_use
    FROM task_events WHERE day BETWEEN toDate(toTimeZone(now(), 'UTC')) - 29 AND toDate(toTimeZone(now(), 'UTC'))
    GROUP BY segment, installation
)
SELECT segment, count() AS launched,
    countIf(last_use >= first_open) AS acted_after_open,
    round(100.0 * acted_after_open / launched, 1) AS conversion_pct
FROM installs WHERE opens > 0 GROUP BY segment ORDER BY segment LIMIT 3
"@
    New-Insight 'Стадии использования' 'Доли установок по стадиям. Начал: ≥3 действия в ≥2 дня, регулярно: ≥5 в ≥3 дня, обе стадии за 7 календарных дней UTC.' @"
WITH task_events AS ($taskEvents),
installs AS (
    SELECT segment, installation,
        uniqExactIf(uuid, event != 'app_opened') AS actions_total,
        uniqExactIf(uuid, event != 'app_opened' AND day >= toDate(toTimeZone(now(), 'UTC')) - 6) AS recent_actions,
        uniqExactIf(day, event != 'app_opened' AND day >= toDate(toTimeZone(now(), 'UTC')) - 6) AS recent_days
    FROM task_events WHERE day <= toDate(toTimeZone(now(), 'UTC')) GROUP BY segment, installation
),
stages AS (
    SELECT segment,
        multiIf(actions_total = 0, 'Запустил',
            recent_actions >= 5 AND recent_days >= 3, 'Регулярно использует',
            recent_actions >= 3 AND recent_days >= 2, 'Начал использовать',
            recent_actions = 0, 'Нет действий 7 дней',
            actions_total <= 2, 'Попробовал',
            'Активен ниже порога') AS stage,
        count() AS installations
    FROM installs GROUP BY segment, stage
)
SELECT segment, stage, installations,
    round(100.0 * installations / sum(installations) OVER (PARTITION BY segment), 1) AS share_pct
FROM stages ORDER BY segment, installations DESC LIMIT 18
"@
    New-Insight 'Возвращаемость · D1 / D7' 'Когорты первого полезного действия за 30 дней UTC. Возврат считается по полезному действию в точный день. Незавершённые окна исключены.' @"
WITH task_events AS ($taskEvents),
activity AS (
    SELECT segment, installation, min(day) AS first_day, groupUniqArray(day) AS days
    FROM task_events WHERE event != 'app_opened' GROUP BY segment, installation
),
cohorts AS (
    SELECT segment,
        countIf(first_day + 1 < toDate(toTimeZone(now(), 'UTC'))) AS eligible_d1,
        countIf(first_day + 1 < toDate(toTimeZone(now(), 'UTC')) AND has(days, first_day + 1)) AS returned_d1,
        countIf(first_day + 7 < toDate(toTimeZone(now(), 'UTC'))) AS eligible_d7,
        countIf(first_day + 7 < toDate(toTimeZone(now(), 'UTC')) AND has(days, first_day + 7)) AS returned_d7
    FROM activity WHERE first_day BETWEEN toDate(toTimeZone(now(), 'UTC')) - 30 AND toDate(toTimeZone(now(), 'UTC')) - 1
    GROUP BY segment
)
SELECT segment, eligible_d1, returned_d1, round(100.0 * returned_d1 / nullIf(eligible_d1, 0), 1) AS D1_pct,
    eligible_d7, returned_d7, round(100.0 * returned_d7 / nullIf(eligible_d7, 0), 1) AS D7_pct
FROM cohorts ORDER BY segment LIMIT 3
"@
)

if ($DefinitionsOnly) {
    ConvertTo-Json -InputObject $insights -Depth 30
    return
}

if (-not $PostHogHost -or -not $ProjectId -or $env:TASK_POSTHOG_MANAGEMENT_KEY -notmatch '^phx_[A-Za-z0-9_-]+$') {
    throw 'Set TASK_POSTHOG_MANAGEMENT_KEY and specify -PostHogHost and -ProjectId. Use a scoped personal API key for this script.'
}

$api = "$PostHogHost/api/projects/$ProjectId"
$headers = @{ Authorization = "Bearer $($env:TASK_POSTHOG_MANAGEMENT_KEY)" }

function Invoke-PostHog($Method, $Path, $Payload = $null) {
    $parameters = @{
        Method = $Method
        Uri = "$api/$Path"
        Headers = $headers
        MaximumRedirection = 0
        TimeoutSec = 30
    }
    if ($null -ne $Payload) {
        $parameters.ContentType = 'application/json; charset=utf-8'
        $parameters.Body = [Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $Payload -Depth 30 -Compress))
    }
    Invoke-RestMethod @parameters
}

$search = [Uri]::EscapeDataString($dashboardName)
$match = @( (Invoke-PostHog Get "dashboards/?search=$search").results | Where-Object name -CEQ $dashboardName )
if ($match.Count -gt 1) { throw 'Multiple dashboards have the exact Task name. Resolve the duplicate before running this script.' }
$created = $match.Count -eq 0
$dashboard = if ($created) {
    Invoke-PostHog Post 'dashboards/' @{ name = $dashboardName; description = $dashboardDescription; tags = @('task', 'product-usage') }
} else { $match[0] }
$dashboardPath = "dashboards/$($dashboard.id)/"
$dashboard = Invoke-PostHog Get $dashboardPath
foreach ($insight in $insights) {
    $existing = @($dashboard.tiles | Where-Object { $_.insight.name -ceq $insight.name })
    if ($existing.Count -gt 1) { throw "Multiple tiles named $($insight.name)." }
    if ($existing.Count -eq 0) {
        $insight.dashboards = @($dashboard.id)
        $null = Invoke-PostHog Post 'insights/' $insight
    }
}
if ($created) {
    $dashboard = Invoke-PostHog Get $dashboardPath
    $layout = @(
        @{ x = 0; y = 0; w = 6; h = 4 },
        @{ x = 6; y = 4; w = 6; h = 5 },
        @{ x = 0; y = 4; w = 6; h = 5 },
        @{ x = 6; y = 0; w = 6; h = 4 },
        @{ x = 0; y = 9; w = 6; h = 5 },
        @{ x = 6; y = 9; w = 6; h = 5 }
    )
    $tiles = for ($index = 0; $index -lt $insights.Count; $index++) {
        $tile = $dashboard.tiles | Where-Object { $_.insight.name -ceq $insights[$index].name }
        if ($tile) { @{ id = $tile.id; layouts = @{ sm = $layout[$index] }; show_description = $false } }
    }
    $modeFilter = @{ type = 'event'; key = 'mode'; operator = 'exact'; value = @('personal', 'corporate') }
    $null = Invoke-PostHog Patch $dashboardPath @{
        tiles = @($tiles)
        filters = @{ properties = @{ type = 'AND'; values = @(@{ type = 'AND'; values = @($modeFilter) }) } }
    }
}
Write-Output "$PostHogHost/project/$ProjectId/dashboard/$($dashboard.id)"
