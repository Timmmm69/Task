# Упрощение пользовательского пути Task — 1.0.0

Восемь согласованных улучшений личного и корпоративного пространства: выбор режима, объяснения разделов, обязательность полей, доступная дата без времени, точные подписи сохранения, действия пустых экранов, объяснения ограничений и видимый результат сохранения с переходом к задаче.

Проверка из корня репозитория:

```powershell
dotnet build work/production/src/Task.Desktop/Task.Desktop.csproj -c Release --no-restore
dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --no-restore --settings work/ux_journey_1/desktop.runsettings --filter 'FullyQualifiedName!~ShellShortcut_KeyboardOnly_FocusTrapAndRestore' --logger 'trx;LogFileName=desktop-gate.trx' --results-directory work/ux_journey_1/evidence
dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --no-build --settings work/ux_journey_1/desktop.runsettings --filter 'FullyQualifiedName~ShellShortcut_KeyboardOnly_FocusTrapAndRestore' --logger 'trx;LogFileName=focus-gate.trx' --results-directory work/ux_journey_1/evidence
pwsh -NoProfile -File work/production/verification/Test-DesktopShell.ps1
dotnet run --project work/ux_journey_1/UiProbe/UiProbe.csproj -- work/ux_journey_1/evidence/native-accepted
python -X utf8 work/ux_journey_1/package.py
```

Запуски выполнять последовательно. Тест системного клавиатурного фокуса вынесен в отдельный запуск: он чувствителен к активации других окон на рабочем столе. Повторный native probe требует нового пустого каталога evidence, чтобы синтетические задачи предыдущего запуска не изменили пустые состояния.

Native probe использует настоящие WPF-окна и изолированные SQLite-фикстуры. Corporate проверяется через существующую оболочку и локальный presentation adapter; запросы к серверу и пользовательские credentials не используются. PNG показывают client area, включая прокрутку на минимальном размере. 144 DPI — отдельный WPF render, не физический mixed-DPI walkthrough.

Итоговый пакет — `outputs/20261010_task_ux_journey_1.0.0/`. Он содержит source overlay, evidence, manifest и контрольные суммы; это пакет изменений и проверки, а не новый portable release.
