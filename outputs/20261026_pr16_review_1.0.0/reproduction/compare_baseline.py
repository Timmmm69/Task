from pathlib import Path
import os, subprocess, json

root=Path(__file__).resolve().parent
repo=root/'repo'
paths=['work/production/src/Task.Desktop/Views/CalendarView.xaml','work/production/tests/Task.Desktop.Tests/Calendar/CalendarPickerWindowsTests.cs']
saved={p:(repo/p).read_bytes() for p in paths}
try:
    for p in paths:
        (repo/p).write_bytes(subprocess.check_output(['git','show','origin/main:'+p],cwd=repo))
    with (root/'evidence/format-baseline.log').open('w',encoding='utf-8') as out:
        result=subprocess.run(['dotnet','format','whitespace','Task.sln','--verify-no-changes','--no-restore','--verbosity','quiet'],cwd=repo/'work/production',stdout=out,stderr=subprocess.STDOUT)
    with (root/'evidence/baseline-probe-build.log').open('w',encoding='utf-8') as out:
        subprocess.run(['dotnet','build','Probe.csproj','-c','Release','--nologo','-v','quiet'],cwd=root/'probe',stdout=out,stderr=subprocess.STDOUT,check=True)
    env=os.environ|{'PR16_KEYBOARD_ONLY':'1','PR16_AUTO_EXIT':'1'}
    subprocess.run([str(root/'probe/bin/Release/net10.0-windows10.0.17763.0/Probe.exe')],env=env,check=True)
    (root/'evidence/keyboard-baseline.json').write_bytes((root/'evidence/probe/keyboard.json').read_bytes())
    print('Baseline format exit:',result.returncode)
    print('Baseline keyboard:',(root/'evidence/keyboard-baseline.json').read_text())
finally:
    for p,data in saved.items(): (repo/p).write_bytes(data)
    with (root/'evidence/restored-probe-build.log').open('w',encoding='utf-8') as out:
        subprocess.run(['dotnet','build','Probe.csproj','-c','Release','--nologo','-v','quiet'],cwd=root/'probe',stdout=out,stderr=subprocess.STDOUT,check=True)
