Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\..\scripts\Delegation.Common.ps1"

$passed = 0
$failed = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Test-Case([string]$Name, [scriptblock]$Body) {
    try { & $Body; $script:passed++; Write-Host "PASS $Name" }
    catch { $script:failed++; Write-Host "FAIL $Name :: $($_.Exception.Message)" }
}
function Assert-Throws([scriptblock]$Body, [string]$Pattern) {
    try { & $Body; throw "Expected failure matching '$Pattern'." }
    catch {
        if ($_.Exception.Message -notmatch $Pattern) { throw }
    }
}
function New-TestPacket([string]$Owned = "work/sandbox/**") {
    return [ordered]@{
        task_id="TASK-TEST"; title="Test task"; base_branch="main"; base_sha="0000000";
        risk="low"; merge="automatic"; owned_paths=[Collections.Generic.List[string]]@($Owned);
        forbidden_paths=[Collections.Generic.List[string]]@("sources/**", "outputs/**", ".github/**");
        max_files="3"; max_changed_lines="150";
        requirements=[Collections.Generic.List[string]]@("Change one fixture");
        acceptance=[Collections.Generic.List[string]]@("Fixture is updated");
        required_checks=[Collections.Generic.List[string]]@("Write-Output ok");
        reference_files=[Collections.Generic.List[string]]@("work/sandbox/input.txt");
        stop_conditions=[Collections.Generic.List[string]]@("Stop outside scope")
    }
}

Test-Case "template parses and validates" {
    $template = Get-Content -Raw -Encoding UTF8 "$PSScriptRoot\..\templates\DELEGATION_PACKET.yaml"
    $packet = ConvertFrom-DelegationYaml $template
    Assert-True (Test-DelegationPacket $packet) "Template should be valid."
}
Test-Case "missing required field is rejected" {
    $packet = New-TestPacket
    $packet.Remove("acceptance")
    Assert-Throws { Test-DelegationPacket $packet } "required field 'acceptance'"
}
Test-Case "low risk cannot request review route" {
    $packet = New-TestPacket
    $packet.merge = "codex-review"
    Assert-Throws { Test-DelegationPacket $packet } "low risk requires automatic"
}
Test-Case "owned paths must remain under work" {
    $packet = New-TestPacket -Owned "src/**"
    Assert-Throws { Test-DelegationPacket $packet } "under work"
}

$sandbox = Join-Path ([IO.Path]::GetTempPath()) ("task-delegation-tests-" + [Guid]::NewGuid().ToString("N"))
try {
    New-Item -ItemType Directory -Force "$sandbox\work\sandbox", "$sandbox\sources" | Out-Null
    & git -C $sandbox init -q
    & git -C $sandbox config user.email "delegation-tests@example.invalid"
    & git -C $sandbox config user.name "Delegation Tests"
    Set-Content -Encoding UTF8 "$sandbox\work\sandbox\input.txt" "before"
    Set-Content -Encoding UTF8 "$sandbox\sources\canonical.txt" "protected"
    & git -C $sandbox add .
    & git -C $sandbox commit -qm baseline
    $base = (& git -C $sandbox rev-parse HEAD).Trim()

    Test-Case "valid working-tree diff passes" {
        Set-Content -Encoding UTF8 "$sandbox\work\sandbox\input.txt" "after"
        $result = Test-DelegationDiff -Packet (New-TestPacket) -BaseRef $base -RepositoryPath $sandbox -IncludeWorkingTree
        Assert-True ($result.file_count -eq 1) "Expected exactly one changed file."
        & git -C $sandbox checkout -q -- .
    }
    Test-Case "outside owned paths is rejected" {
        Set-Content -Encoding UTF8 "$sandbox\work\outside.txt" "outside"
        Assert-Throws { Test-DelegationDiff -Packet (New-TestPacket) -BaseRef $base -RepositoryPath $sandbox -IncludeWorkingTree } "outside owned_paths"
        Remove-Item -LiteralPath "$sandbox\work\outside.txt"
    }
    Test-Case "canonical source modification is rejected" {
        $packet = New-TestPacket -Owned "work/**"
        $packet.owned_paths = [Collections.Generic.List[string]]@("work/**", "sources/**")
        Set-Content -Encoding UTF8 "$sandbox\sources\canonical.txt" "changed"
        Assert-Throws { Test-DelegationDiff -Packet $packet -BaseRef $base -RepositoryPath $sandbox -IncludeWorkingTree } "forbidden path"
        & git -C $sandbox checkout -q -- .
    }
} finally {
    if (Test-Path $sandbox) { Remove-Item -LiteralPath $sandbox -Recurse -Force }
}

Write-Host "$passed passed, $failed failed"
if ($failed) { exit 1 }
