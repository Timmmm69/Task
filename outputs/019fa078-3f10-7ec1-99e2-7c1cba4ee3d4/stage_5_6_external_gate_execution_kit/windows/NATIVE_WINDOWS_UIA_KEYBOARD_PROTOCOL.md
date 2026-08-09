# Протокол Windows UIA и keyboard-only проверки

## Предпосылки

- Использовать только compiled Windows desktop client, а не browser prototype.
- Тестируемая ревизия: 6a16be2fb371d41af0540569c77daf59eb902a9d.
- Утверждённый клиент: Task Gate 5.6 Client 0.1.1 portable x64, SHA-256 8B047DD69E1A64269F8961FE0416727E5083E0C2B30285A73DD2E92A2D412E53.
- Зафиксировать версию приложения, сборку Windows, locale, роль, серверный fixture и timestamp.
- Использовать production-like authorized data без клиентских секретов.

## Процедура

Для каждой строки windows/Windows_Accessibility_Checkpoints.csv:

1. Снять через Inspect.exe свойства UIA: имя, control type, state/value, selection/expanded/current, если применимо.
2. Пройти заданный keyboard path без мыши и записать порядок фокуса и возврат фокуса.
3. Приложить ссылку на screenshot или screen recording и идентификаторы находок.
4. Поставить PASS только когда показаны ожидаемые UIA-свойства, клавиатурный путь и пользовательский исход.

## Условия остановки

Остановить прогон и завести Critical/High при focus trap, недоступном обязательном действии, нераскрытом последствии опасного действия, раскрытии прав/данных, принятой записи в offline/read-only или потере пользовательского ввода. Gate 5.6 не подписывается при открытом Critical/High.
