# Task — пять улучшений форм, 1.0.0

Календарь WPF дополняет строгий ручной ввод даты без времени. Сегодня/Завтра используют ScheduleZone либо локальную зону; точное начало сохраняет владение днём. Enter отправляет короткую запись; Ctrl+Enter использует команды видимой формы задач/проектов. Обычный Enter остаётся переносом строки. Checkbox возвращается к подтверждённым данным до выполнения существующей команды. Фильтр Personal действует только в «Задачах», по умолчанию выключен и не сохраняется между запусками.

Последовательная проверка из корня:

```powershell
dotnet build work/production/src/Task.Desktop/Task.Desktop.csproj -c Release --no-restore
dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --no-restore --settings work/ux_journey_1/desktop.runsettings --filter 'FullyQualifiedName!~ShellShortcut_KeyboardOnly_FocusTrapAndRestore'
dotnet test work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj --no-build --settings work/ux_journey_1/desktop.runsettings --filter 'FullyQualifiedName~ShellShortcut_KeyboardOnly_FocusTrapAndRestore'
pwsh -NoProfile -File work/production/verification/Test-DesktopShell.ps1
dotnet run --project work/ux_journey_1/UiProbe/UiProbe.csproj -- work/ux_five_1/evidence/new-empty-native-directory
python work/ux_five_1/package.py
```

UiProbe открывает настоящие WPF-окна и посылает системные клавиши последовательно. Повторный запуск требует нового каталога фикстур. Corporate использует локальные presentation adapters без сети/credentials. 144 DPI — WPF render, не проверка физического mixed-DPI окружения. Системный фокус требует активного рабочего стола без конкурирующих UI-прогонов.

Пакет содержит source overlay, manifest, SHA-256, отчёт и фактические evidence; это не portable release. Обновление dashboard сохраняет чужие рабочие изменения, а `roadmap-for-index.json` содержит только три затронутых пункта поверх HEAD.
