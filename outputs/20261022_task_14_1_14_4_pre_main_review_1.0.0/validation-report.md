# Проверка 14.1–14.4 перед main

Версия: 1.0.0. Дата: 22.10.2026 (Europe/Minsk).

Проверенный commit: `0491410dd2607874de731b9d36abedd949966922`; baseline: `fa504bc`.

Полный gate: **FAIL**. Push в main не выполнялся.

Production-код при этой проверке не изменялся. Проверены контракт возврата, серверный diff, права/сессии, атомарность и идемпотентность; отдельный read-only reviewer проверил WPF retry/late response/filter/focus/layout. Новых конкретных дефектов исходников в проверенном diff не найдено.

Release build: PASS, 0 errors. Dashboard validation: PASS. Пять пакетов: 209 SHA256SUMS записей и manifest entries проверены; ZIP 14.4, CRC/inventory/payload и source hashes совпали.

| Suite | Total | PASS | FAIL | Not executed |
|---|---:|---:|---:|---:|
| novik_TIM_2026-10-22_18_14_55_net10.0.trx | 905 | 905 | 0 | 0 |
| novik_TIM_2026-10-22_18_18_20_net10.0.trx | 669 | 669 | 0 | 0 |
| novik_TIM_2026-10-22_18_18_39_net10.0.trx | 1822 | 1821 | 1 | 0 |

## Непройденная проверка: Task.Desktop.Tests.TaskScreen.TaskContextActionsUiTests.NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow

```text
ðÆðÁÐÇÐüð©ÐÅ VSTest 18.7.0 (x64)

ð×ð▒ÐëðÁðÁ ð║ð¥ð╗ð©ÐçðÁÐüÐéð▓ð¥ ÐéðÁÐüÐéð¥ð▓ÐïÐà Ðäð░ð╣ð╗ð¥ð▓ (1), Ðüð¥ð¥Ðéð▓ðÁÐéÐüÐéð▓ÐâÐÄÐëð©Ðà Ðâð║ð░ðÀð░ð¢ð¢ð¥ð╝Ðâ Ðêð░ð▒ð╗ð¥ð¢Ðâ.
  ðØðÁ ð┐ÐÇð¥ð╣ð┤ðÁð¢ Task.Desktop.Tests.TaskScreen.TaskContextActionsUiTests.NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow [1 s]
  ðíð¥ð¥ð▒ÐëðÁð¢ð©ðÁ ð¥ð▒ ð¥Ðêð©ð▒ð║ðÁ:
   Assert.True() Failure
Expected: True
Actual:   False
  ðóÐÇð░ÐüÐüð©ÐÇð¥ð▓ð║ð░ ÐüÐéðÁð║ð░:
     at Task.Desktop.Tests.TaskScreen.TaskContextActionsUiTests.<>c__DisplayClass0_1.<<NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow>b__1>d.MoveNext() in C:\Users\novik\ðóð░Ðüð║\work\status_return_14_4\repo\work\production\tests\Task.Desktop.Tests\Tasks\TaskContextActionsUiTests.cs:line 62
--- End of stack trace from previous location ---
   at Task.Desktop.Tests.TaskScreen.TaskContextActionsUiTests.NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow() in C:\Users\novik\ðóð░Ðüð║\work\status_return_14_4\repo\work\production\tests\Task.Desktop.Tests\Tasks\TaskContextActionsUiTests.cs:line 140
   at Task.Desktop.Tests.TaskScreen.TaskContextActionsUiTests.NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow() in C:\Users\novik\ðóð░Ðüð║\work\status_return_14_4\repo\work\production\tests\Task.Desktop.Tests\Tasks\TaskContextActionsUiTests.cs:line 141
--- End of stack trace from previous location ---

ðØðÁ ð┐ÐÇð¥ð╣ð┤ðÁð¢!: ð¢ðÁ ð┐ÐÇð¥ð╣ð┤ðÁð¢ð¥     1, ð┐ÐÇð¥ð╣ð┤ðÁð¢ð¥     0, ð┐ÐÇð¥ð┐ÐâÐëðÁð¢ð¥     0, ð▓ÐüðÁð│ð¥     1, ð┤ð╗ð©ÐéðÁð╗Ðîð¢ð¥ÐüÐéÐî 1 s. - Task.Desktop.Tests.dll (net10.0)
[xUnit.net 00:00:01.30]     Task.Desktop.Tests.TaskScreen.TaskContextActionsUiTests.NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow [FAIL]

   at Task.Desktop.Tests.TaskScreen.TaskContextActionsUiTests.NativeWindows_MouseKeyboardFocusRelationsAndSmallWindow() in C:\Users\novik\Таск\work\status_return_14_4\repo\work\production\tests\Task.Desktop.Tests\Tasks\TaskContextActionsUiTests.cs:line 34
--- End of stack trace from previous location ---
```

## Ограничения

Исторический аудит 14.4 остаётся неизменным. API-14.4-07 (HistoryEntry DTO drift) и DOC-14.4-08 (каталог ошибок/OpenAPI) остаются прежними открытыми вопросами вне узкого возврата. LAN/multi-client и screen reader вручную не проверены. Serena не ответила; использован scoped diff/read fallback.

Если полный gate красный, условие пользователя для push не выполнено; ошибки не подавлялись и тесты не исключались.
