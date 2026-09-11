# HAND-03 — воспроизводимый и проверяемый release candidate

Пакет объединяет один зафиксированный Git revision и все передаваемые компоненты Task:

- воспроизводимые `linux/amd64` OCI-образы API, worker, backup agent и migrator с evidence двух независимых сборок;
- self-contained Windows x64 desktop release с Authenticode и подписанными release/channel manifests;
- архив `work/production` из указанного Git tree;
- SPDX 2.3 SBOM и сводную таблицу заявленных лицензий NuGet-зависимостей;
- полный `manifest.json`, `SHA256SUMS`, detached CMS-подпись и публичный сертификат проверяющего подписанта;
- независимый validation report и автономный `Verify-Release.ps1`.

## Проверка

На Windows с PowerShell 7 и .NET 10 выполнить из родительского каталога пакета:

```powershell
pwsh -NoProfile -File .\<package>\Verify-Release.ps1 -ReleaseDirectory .\<package>
```

Проверка fail-closed пересчитывает размер и SHA-256 каждого файла, сверяет обе инвентаризации, CMS-подпись и pin сертификата, SPDX/лицензии, OCI digest map, source Git binding и подписи Windows release. При наличии соседнего ZIP он повторно открывается и полностью читается.

## Воспроизведение

Исходный архив воспроизводится командой `git archive` из `sourceRevision` и `productionTree` с `sourceDateEpoch`. Серверные образы уже построены дважды в независимых пустых BuildKit builders; равенство manifest/config/layer digests зафиксировано в server evidence. Windows payload воспроизводится скриптом `deployment/desktop/Build-TaskDesktopRelease.ps1` из того же source tree; криптографическая подпись и channel metadata ожидаемо зависят от выбранного подписанта и момента публикации.

## Граница доверия и production-публикация

Внутренний RC подписан временным self-signed validation-сертификатом, публичная часть которого включена в пакет и закреплена thumbprint. Это обеспечивает проверяемую целостность и единство подписанта, но не удостоверяет корпоративного издателя. Закрытый ключ не сохраняется ни в репозитории, ни в пакете.

Перед production-публикацией оператор обязан пересобрать Windows-компонент на signing workstation с корпоративным code-signing сертификатом и RFC 3161 timestamp, повторно выполнить gate, разместить ZIP/channel на контролируемом HTTPS origin и сохранить pilot evidence согласно `OPS-05-windows-client-release.md` и `OPS-operations-runbook.md`.
