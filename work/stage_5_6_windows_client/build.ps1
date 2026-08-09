$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$prototype = Join-Path $repoRoot 'work\stage_5_prototype'
$client = $PSScriptRoot
$npm = (Get-Command npm.cmd -ErrorAction Stop).Source
$node = (Get-Command node.exe -ErrorAction Stop).Source

Push-Location $prototype
try {
  & $npm ci
  if ($LASTEXITCODE -ne 0) { throw 'Prototype npm ci failed.' }
  & $npm run build
  if ($LASTEXITCODE -ne 0) { throw 'Prototype production build failed.' }
  $tests = Get-ChildItem -LiteralPath (Join-Path $prototype 'tests') -Filter '*.test.mjs' | ForEach-Object FullName
  & $node --test @tests
  if ($LASTEXITCODE -ne 0) { throw 'Prototype test suite failed.' }
  $env:TASK_DESKTOP_BUILD = '1'
  & $npm exec vite build
  if ($LASTEXITCODE -ne 0) { throw 'Electron renderer build failed.' }
  $electronIndex = Join-Path $client 'runtime\client\index.html'
  $electronMarkup = Get-Content -LiteralPath $electronIndex -Raw
  if ($electronMarkup -notmatch '(src|href)="\./assets/') { throw 'Electron renderer must use relative asset URLs.' }
  Remove-Item Env:TASK_DESKTOP_BUILD -ErrorAction SilentlyContinue
}
finally {
  Remove-Item Env:TASK_DESKTOP_BUILD -ErrorAction SilentlyContinue
  Pop-Location
}

Push-Location $client
try {
  & $npm ci
  if ($LASTEXITCODE -ne 0) { throw 'Desktop client npm ci failed.' }
  & $npm test
  if ($LASTEXITCODE -ne 0) { throw 'Desktop fixture tests failed.' }
  & $npm run dist
  if ($LASTEXITCODE -ne 0) { throw 'Windows portable build failed.' }
}
finally {
  Pop-Location
}

$artifact = Join-Path $client 'dist\Task-Gate-5.6-Client-0.1.2-win-x64.exe'
if (-not (Test-Path -LiteralPath $artifact)) { throw "Expected artifact not found: $artifact" }
Get-FileHash -Algorithm SHA256 -LiteralPath $artifact
