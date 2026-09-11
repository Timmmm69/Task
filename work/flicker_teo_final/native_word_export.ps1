$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $MyInvocation.MyCommand.Path
$out=[IO.Path]::GetFullPath((Join-Path $taskRoot '../../outputs/flicker_teo_final/output'))
$word=New-Object -ComObject Word.Application
$document=$null
try {
    $word.Visible=$false;$word.DisplayAlerts=0
    $word.AutomationSecurity=3
    $document=$word.Documents.Open((Join-Path $out 'Flicker_TEO_FINAL.docx'),$false,$true)
    $document.Repaginate()
    [void]$document.Fields.Update()
    $document.ExportAsFixedFormat((Join-Path $out 'Flicker_TEO_FINAL.pdf'),17)
    [pscustomobject]@{engine='Microsoft Word';version=$word.Version;pages=$document.ComputeStatistics(2);source='Flicker_TEO_FINAL.docx';output='Flicker_TEO_FINAL.pdf'} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRoot 'qa/word_export.json') -Encoding utf8
    Write-Output "Word exported $($document.ComputeStatistics(2)) pages."
} finally {
    if($null -ne $document){$document.Close(0);[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($document)}
    $word.Quit();[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($word)
}
