$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $MyInvocation.MyCommand.Path
$excel=New-Object -ComObject Excel.Application
$book=$null
$results=[System.Collections.Generic.List[object]]::new()
function Check([string]$name,[bool]$passed,$value) { $results.Add([pscustomobject]@{test=$name;passed=$passed;value=$value}) }
function NoErrors([string]$label) {
    $found=@()
    foreach($sheet in $book.Worksheets) {
        try {
            $bad=$sheet.UsedRange.SpecialCells(-4123,16)
            if($null -ne $bad){$found += "$($sheet.Name): $($bad.Address())"}
        } catch {
            if($_.Exception.HResult -ne -2146827284){throw}
        }
    }
    Check "$label formula errors" ($found.Count -eq 0) ($found -join '; ')
}
try {
    $excel.Visible=$false;$excel.DisplayAlerts=$false;$excel.EnableEvents=$false;$excel.AutomationSecurity=3
    $book=$excel.Workbooks.Open((Join-Path $taskRoot 'qa/Flicker_TEO_recalculated.xlsx'),0,$true)
    $input=$book.Worksheets.Item('Вводные');$sc=$book.Worksheets.Item('Сценарии');$ret=$book.Worksheets.Item('Удержание')
    $excel.CalculateFullRebuild();$baseline=[double]$sc.Range('D7').Value2
    $probe=$book.Worksheets.Item('QA').Range('Z100');$probe.Formula='=1/0';$excel.CalculateFull()
    $probeErrors=$book.Worksheets.Item('QA').UsedRange.SpecialCells(-4123,16)
    Check 'Error scanner detects deliberate division by zero' ($probeErrors.Address() -like '*Z*100*') $probeErrors.Address()
    $probe.ClearContents();$excel.CalculateFull()
    NoErrors 'Baseline'
    $input.Range('E13').Value2=0;$excel.CalculateFull()
    Check 'Zero participation gives zero prevented exits' ($ret.Range('D17').Value2 -eq 0) $ret.Range('D17').Value2
    NoErrors 'Zero participation';$input.Range('E13').Value2=.7
    $input.Range('E30').Value2=0;$excel.CalculateFull()
    Check 'Time cost can be disabled visibly' ($book.Worksheets.Item('Время участников').Range('K18').Value2 -eq 0) $sc.Range('D14').Value2
    NoErrors 'Time disabled';$input.Range('E30').Value2=1
    $input.Range('E17').Value2=0;$excel.CalculateFull()
    Check 'Zero causal effect gives zero benefit' ($sc.Range('D13').Value2 -eq 0) $sc.Range('D13').Value2
    NoErrors 'Zero causal';$input.Range('E17').Value2=.3
    $input.Range('E21').Value2=0;$excel.CalculateFull()
    Check 'Zero discount NPV equals cumulative flow' ([Math]::Abs($sc.Range('D7').Value2-$sc.Range('D26').Value2) -lt .02) $sc.Range('D7').Value2
    NoErrors 'Zero discount';$input.Range('E21').Value2=.2
    $input.Range('E41').Value2=2;$excel.CalculateFull()
    Check 'Unknown ITT not silently zero NPV' ($sc.Range('D7').Value2 -eq 'не рассчитано') $sc.Range('D7').Value2
    NoErrors 'Unknown ITT'
    $input.Range('E42').Value2=.001185408;$excel.CalculateFull()
    Check 'ITT ARR is not multiplied by funnel again' ([Math]::Abs($sc.Range('D7').Value2-$baseline) -lt .02) $sc.Range('D7').Value2
    NoErrors 'ITT measured';$input.Range('E41').Value2=1;$input.Range('E42').ClearContents()
    $input.Range('E19').Value2=100;$excel.CalculateFull()
    Check 'Positive economics test has positive NPV' ($sc.Range('D7').Value2 -gt 0) $sc.Range('D7').Value2
    Check 'IRR calculation works with sign change' ($sc.Range('D9').Value2 -is [double]) $sc.Range('D9').Value2
    Check 'Payback is after project start' (($sc.Range('D11').Value2 -is [double]) -and $sc.Range('D11').Value2 -gt 0) $sc.Range('D11').Value2
    NoErrors 'Positive hypothetical test';$input.Range('E19').Value2=1
    $input.Range('C4').Value2=3;$excel.CalculateFull()
    Check 'Scenario comparisons independent of selector' ([Math]::Abs($sc.Range('D7').Value2-$baseline) -lt .02) $sc.Range('D7').Value2
    $input.Range('C4').Value2=2
    $mc=$book.Worksheets.Item('Вероятностный анализ');$median=$mc.Range('C9').Value2
    $mc.Range('D25').Value2=.25;$excel.CalculateFull()
    Check 'Editing MC distribution changes result' ([Math]::Abs($mc.Range('C9').Value2-$median) -gt 1) $mc.Range('C9').Value2
    NoErrors 'MC parameter edit';$mc.Range('D25').Value2=.15
    $excel.CalculateFull();Check 'Restored baseline unchanged' ([Math]::Abs($sc.Range('D7').Value2-$baseline) -lt .02) $sc.Range('D7').Value2
    $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskRoot 'qa/input_tests.json') -Encoding utf8
    $results | Where-Object {-not $_.passed} | Format-Table -AutoSize
    Write-Output "Tests: $($results.Count); failed: $(@($results | Where-Object {-not $_.passed}).Count)"
} finally {
    if($null -ne $book){$book.Close($false);[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($book)}
    $excel.Quit();[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)
}
