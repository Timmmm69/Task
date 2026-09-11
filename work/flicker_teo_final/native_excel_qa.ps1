param([switch]$Render)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$qa = Join-Path $taskRoot 'qa'
$source = Join-Path $qa 'Flicker_TEO_working.xlsx'
$target = Join-Path $qa 'Flicker_TEO_recalculated.xlsx'
$excel = New-Object -ComObject Excel.Application
$book = $null
try {
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.EnableEvents = $false
    $excel.AutomationSecurity = 3
    $book = $excel.Workbooks.Open($source, 0, $false)
    $excel.CalculateFullRebuild()
    $map = Get-Content -LiteralPath (Join-Path $qa 'workbook_map.json') -Raw | ConvertFrom-Json
    $stats = @()
    foreach ($sheet in $book.Worksheets) {
        $stats += [pscustomobject]@{ sheet=$sheet.Name; rows=$sheet.UsedRange.Rows.Count; columns=$sheet.UsedRange.Columns.Count }
    }
    $book.SaveAs($target, 51)
    $stats | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $qa 'native_excel_stats.json') -Encoding utf8
    Write-Output "Recalculated with Excel $($excel.Version) and saved $target"
    if ($Render) {
        $pdfDir = Join-Path $qa 'sheets'
        [void](New-Item -ItemType Directory -Path $pdfDir -Force)
        $i=0
        foreach ($sheet in $book.Worksheets) {
            $i++
            $range = $map.ranges.($sheet.Name)
            $sheet.Range($range).WrapText = $true
            $sheet.Range($range).Rows.AutoFit()
            $sheet.PageSetup.PrintArea = $range
            $sheet.PageSetup.Orientation = 2
            $sheet.PageSetup.PaperSize = 8
            $sheet.PageSetup.Zoom = $false
            $sheet.PageSetup.FitToPagesWide = 1
            $sheet.PageSetup.FitToPagesTall = $false
            $sheet.PageSetup.LeftMargin = 18
            $sheet.PageSetup.RightMargin = 18
            $sheet.PageSetup.TopMargin = 18
            $sheet.PageSetup.BottomMargin = 24
            $sheet.PageSetup.CenterFooter = "$($sheet.Name)  |  &P"
            $sheet.ExportAsFixedFormat(0, (Join-Path $pdfDir ('sheet-{0:d2}.pdf' -f $i)))
            Write-Output "Rendered sheet $i $($sheet.Name)"
        }
        $book.Save()
    }
} finally {
    if ($null -ne $book) { $book.Close($false); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($book) }
    $excel.Quit()
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)
    [GC]::Collect()
}
