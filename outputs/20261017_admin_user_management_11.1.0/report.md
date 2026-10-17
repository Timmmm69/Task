# Задача 11.1 — API управления сотрудниками, версия 11.1.0

Реализованы полная разрешённая карточка User, курсорные страницы с total и серверным filter,
создание, выборочный PATCH с различением пропуска/null и пять переходов состояния без UI.
Сохранены URI/methods, authenticated executor, strong If-Match, требуемые ключи,
correlation ID, refresh-flow и отмена. Типизированы все требуемые категории ошибок,
сохранены stable codes. Сетевые ошибки не вызывают повторную отправку мутаций.

API/DTO и ограничения: [api-contract.md](api-contract.md). Изменённые файлы: manifest.json.
Исходная база: 2f50889e690d71a2d710ae49d055897d191d655f. Ветка: feature/admin-user-management. В main ничего не внесено.

Проверки: Release solution build PASS; целевые 196/196;
серверные 626/626;
полный desktop 1336 PASS / 1 FAIL,
подтверждающий прогон 1336 PASS / 1 FAIL.
После финальной проверки strict JSON numbers: 1337 PASS / 1 FAIL.
Baseline origin/main: 1277 PASS / 2 FAIL.
Проблемный native WPF-тест воспроизводит сбой в полном baseline, но отдельно проходит
на обеих версиях. Общий desktop gate не объявлен зелёным. Детали — evidence/*.trx.

Права: User.Read/Create/Update/Block сверены с действующими endpoint policies,
существующий серверный набор проверен. User.ManageRoles и User.ResetPassword
зафиксированы, новых клиентских операций для них нет.

API-основа для 11.2 готова в пределах scoped acceptance. Подключение WPF-форм,
live-server/manual acceptance и исправление независимых native WPF-тестов — вне 11.1.
Расхождения accountStatus в схеме/сервере перечислены в api-contract.md.
