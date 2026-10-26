from pathlib import Path
import subprocess
root=Path(__file__).resolve().parent
repo=root/'repo'
path='work/production/tests/Task.Desktop.Tests/Calendar/CalendarPickerWindowsTests.cs'
saved=(repo/path).read_bytes()
try:
    (repo/path).write_bytes(subprocess.check_output(['git','show','origin/main:'+path],cwd=repo))
    with (root/'evidence/resume-format-baseline.log').open('w',encoding='utf-8') as log:
        result=subprocess.run(['dotnet','format','whitespace','Task.sln','--verify-no-changes','--no-restore','--verbosity','quiet'],cwd=repo/'work/production',stdout=log,stderr=subprocess.STDOUT)
    # PowerShell redirects native output with CRLF while Python uses LF.
    assert (root/'evidence/resume-format.log').read_text(encoding='utf-8-sig')==(root/'evidence/resume-format-baseline.log').read_text(encoding='utf-8-sig')
    print('PASS: latest-main baseline and PR formatting diagnostics identical:',len((root/'evidence/resume-format.log').read_text(encoding='utf-8-sig').splitlines()),'exit',result.returncode)
finally:
    (repo/path).write_bytes(saved)
