[CmdletBinding()]
param(
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot 'outputs\20260920_direction2_final_acceptance_1.0.0\direction2-baseline-production-contact-sheet.png'
}
$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
$repoPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $outputFullPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputPath must be inside the Task repository.'
}

Add-Type -AssemblyName System.Drawing.Common

$baselineRoot = Join-Path $repoRoot 'work\stage_5_prototype'
$evidenceRoot = Join-Path $repoRoot 'work\production\evidence\final-direction2-acceptance-1.0.0'
$items = @(
    @{ Title = 'Сегодня · production host 150%'; Baseline = 'implementation-direction2-final.png'; Production = 'auth-admin-settings\today-online.png' },
    @{ Title = 'Задачи · production 100%'; Baseline = 'implementation-direction2-tasks-final.png'; Production = 'desk05\main-100.png' },
    @{ Title = 'Календарь · production 100%'; Baseline = 'implementation-direction2-calendar-week.png'; Production = 'desk05\calendar-100.png' },
    @{ Title = 'Входящие · production host 150%'; Baseline = 'p0-inbox-conversion.png'; Production = 'auth-admin-settings\inbox-online.png' },
    @{ Title = 'Проекты · production host 150%'; Baseline = 'implementation-direction2-projects-final.png'; Production = 'auth-admin-settings\projects-online.png' },
    @{ Title = 'Поиск · production host 150%'; Baseline = 'qa-wave-c-search.png'; Production = 'search-lifecycle\normal.png' },
    @{ Title = 'Настройки · production host 150%'; Baseline = 'qa-wave-c-settings.png'; Production = 'auth-admin-settings\settings-profile.png' },
    @{ Title = 'Администрирование · production host 150%'; Baseline = 'qa-wave-c-admin-users.png'; Production = 'auth-admin-settings\admin-users.png' },
    @{ Title = 'Offline / read-only · production host 150%'; Baseline = 'p0-offline-readonly.png'; Production = 'search-lifecycle\offline.png' }
)

$columns = 2
$cardWidth = 1260
$cardHeight = 700
$gutter = 24
$margin = 32
$headerHeight = 92
$rows = [Math]::Ceiling($items.Count / $columns)
$canvasWidth = ($margin * 2) + ($columns * $cardWidth) + (($columns - 1) * $gutter)
$canvasHeight = $headerHeight + ($margin * 2) + ($rows * $cardHeight) + (($rows - 1) * $gutter)

function Draw-ImageContained {
    param(
        [Drawing.Graphics]$Graphics,
        [Drawing.Image]$Image,
        [Drawing.RectangleF]$Bounds
    )

    $scale = [Math]::Min($Bounds.Width / $Image.Width, $Bounds.Height / $Image.Height)
    $width = [single]($Image.Width * $scale)
    $height = [single]($Image.Height * $scale)
    $x = [single]($Bounds.X + (($Bounds.Width - $width) / 2))
    $y = [single]($Bounds.Y + (($Bounds.Height - $height) / 2))
    $Graphics.DrawImage($Image, $x, $y, $width, $height)
}

$outputDirectory = Split-Path -Parent $outputFullPath
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$bitmap = [Drawing.Bitmap]::new($canvasWidth, $canvasHeight)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
$titleFont = [Drawing.Font]::new('Segoe UI', 25, [Drawing.FontStyle]::Bold)
$cardTitleFont = [Drawing.Font]::new('Segoe UI', 16, [Drawing.FontStyle]::Bold)
$labelFont = [Drawing.Font]::new('Segoe UI', 12, [Drawing.FontStyle]::Bold)
$backgroundBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(245, 247, 250))
$cardBrush = [Drawing.SolidBrush]::new([Drawing.Color]::White)
$textBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(27, 26, 25))
$mutedBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(96, 94, 92))
$borderPen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(225, 223, 221), 2)

try {
    $graphics.Clear([Drawing.Color]::White)
    $graphics.FillRectangle($backgroundBrush, 0, 0, $canvasWidth, $canvasHeight)
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.DrawString('Frozen Direction 2 — baseline / production', $titleFont, $textBrush, $margin, 24)
    $graphics.DrawString('Слева — frozen prototype; справа — Release WPF, deterministic evidence.', $labelFont, $mutedBrush, $margin, 62)

    for ($index = 0; $index -lt $items.Count; $index++) {
        $item = $items[$index]
        $column = $index % $columns
        $row = [Math]::Floor($index / $columns)
        $cardX = $margin + ($column * ($cardWidth + $gutter))
        $cardY = $headerHeight + $margin + ($row * ($cardHeight + $gutter))
        $cardBounds = [Drawing.Rectangle]::new($cardX, $cardY, $cardWidth, $cardHeight)
        $graphics.FillRectangle($cardBrush, $cardBounds)
        $graphics.DrawRectangle($borderPen, $cardBounds)
        $graphics.DrawString($item.Title, $cardTitleFont, $textBrush, $cardX + 22, $cardY + 16)

        $paneGap = 18
        $paneWidth = [single](($cardWidth - 44 - $paneGap) / 2)
        $paneTop = [single]($cardY + 82)
        $paneHeight = [single]($cardHeight - 108)
        $leftBounds = [Drawing.RectangleF]::new($cardX + 22, $paneTop, $paneWidth, $paneHeight)
        $rightBounds = [Drawing.RectangleF]::new($cardX + 22 + $paneWidth + $paneGap, $paneTop, $paneWidth, $paneHeight)
        $graphics.DrawString('BASELINE', $labelFont, $mutedBrush, $leftBounds.X, $cardY + 53)
        $graphics.DrawString('PRODUCTION', $labelFont, $mutedBrush, $rightBounds.X, $cardY + 53)

        $baselinePath = Join-Path $baselineRoot $item.Baseline
        $productionPath = Join-Path $evidenceRoot $item.Production
        if (-not (Test-Path -LiteralPath $baselinePath)) { throw "Missing baseline image: $baselinePath" }
        if (-not (Test-Path -LiteralPath $productionPath)) { throw "Missing production image: $productionPath" }
        $baseline = [Drawing.Image]::FromFile($baselinePath)
        $production = [Drawing.Image]::FromFile($productionPath)
        try {
            Draw-ImageContained -Graphics $graphics -Image $baseline -Bounds $leftBounds
            Draw-ImageContained -Graphics $graphics -Image $production -Bounds $rightBounds
        }
        finally {
            $baseline.Dispose()
            $production.Dispose()
        }
    }

    $bitmap.Save($outputFullPath, [Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $borderPen.Dispose()
    $mutedBrush.Dispose()
    $textBrush.Dispose()
    $cardBrush.Dispose()
    $backgroundBrush.Dispose()
    $labelFont.Dispose()
    $cardTitleFont.Dispose()
    $titleFont.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

Write-Host "[PASS] Direction 2 contact sheet: $outputFullPath"
