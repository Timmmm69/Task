$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $MyInvocation.MyCommand.Path
$chartOut=Join-Path $taskRoot 'qa/native_chart_images'
[void][IO.Directory]::CreateDirectory($chartOut)
$excel=New-Object -ComObject Excel.Application
$book=$null
try {
 $excel.Visible=$false;$excel.DisplayAlerts=$false;$excel.EnableEvents=$false;$excel.AutomationSecurity=3
 $book=$excel.Workbooks.Open((Join-Path $taskRoot 'qa/Flicker_TEO_recalculated.xlsx'),0,$true)
 $excel.CalculateFullRebuild()
 foreach($sheet in $book.Worksheets){
  foreach($chartObject in $sheet.ChartObjects()){
   $sheet.Activate()
   $file=Join-Path $chartOut ($sheet.Name+'.pdf')
   $chartObject.Activate()
   $excel.ActiveChart.ExportAsFixedFormat(0,$file)
   if((Get-Item -LiteralPath $file).Length -le 0){throw "Chart export failed: $($sheet.Name)"}
   Write-Output $file
  }
 }
} finally {
 if($null -ne $book){$book.Close($false);[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($book)}
 $excel.Quit();[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)
}
