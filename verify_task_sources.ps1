$ErrorActionPreference = "Stop"

$required = @(
  "sources\concept\Task_Concept_Final.txt",
  "sources\stage_1\architecture_organizer.md",
  "sources\stage_2_2\Organizer_Stage2_Technical_Specification_2.2.zip",
  "sources\stage_3_4\Organizer_Stage3_Final_Baseline_3.4.zip",
  "sources\stage_4_1_1\Organizer_Stage4_PRD_Candidate_4.1.1.zip"
)

$expected = @{
  "sources\stage_2_2\Organizer_Stage2_Technical_Specification_2.2.zip" = "CC35044B8EADFB6EC4E145CADDB671CF24791AACCB146F2FDF90B4FE440B768D"
  "sources\stage_3_4\Organizer_Stage3_Final_Baseline_3.4.zip" = "BA32E6554E5BD420E1E1AA67BE5B33F678056F26AE34B599A126469FF68D67DB"
  "sources\stage_4_1_1\Organizer_Stage4_PRD_Candidate_4.1.1.zip" = "723E1C665C47D38AE16347085967228AC3DD7CE32CD219692FAD2EF4B49C0168"
}

Write-Host "Проверка проекта Task..." -ForegroundColor Cyan
$missing = @()

foreach ($path in $required) {
  if (-not (Test-Path $path)) {
    Write-Host "НЕТ: $path" -ForegroundColor Red
    $missing += $path
  } else {
    Write-Host "ЕСТЬ: $path" -ForegroundColor Green
  }
}

foreach ($path in $expected.Keys) {
  if (Test-Path $path) {
    $actual = (Get-FileHash -Algorithm SHA256 $path).Hash.ToUpperInvariant()
    if ($actual -eq $expected[$path]) {
      Write-Host "SHA PASS: $path" -ForegroundColor Green
    } else {
      Write-Host "SHA FAIL: $path" -ForegroundColor Red
      Write-Host "  expected: $($expected[$path])"
      Write-Host "  actual:   $actual"
    }
  }
}

if ($missing.Count -gt 0) {
  Write-Host "`nНе хватает файлов: $($missing.Count)" -ForegroundColor Yellow
  exit 1
}

Write-Host "`nВсе обязательные файлы присутствуют." -ForegroundColor Green
