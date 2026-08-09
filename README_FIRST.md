# Что делать

1. Создай или выбери локальную папку проекта Codex с названием `Task`.
   Рекомендуемый путь:
   `C:\Users\novik\Projects\Task`

2. Распакуй содержимое этого архива прямо в корень папки `Task`.

3. Добавь в указанные папки пять обязательных исходников:

   - `sources/concept/Task_Concept_Final.txt`
   - `sources/stage_1/architecture_organizer.md`
   - `sources/stage_2_2/Organizer_Stage2_Technical_Specification_2.2.zip`
   - `sources/stage_3_4/Organizer_Stage3_Final_Baseline_3.4.zip`
   - `sources/stage_4_1_1/Organizer_Stage4_PRD_Candidate_4.1.1.zip`

4. Исходную финальную концепцию можно переименовать в
   `Task_Concept_Final.txt`, не меняя её содержимое.

5. Запусти PowerShell в корне проекта и выполни:
   `powershell -ExecutionPolicy Bypass -File .\verify_task_sources.ps1`

6. Открой папку `Task` в Codex и отправь bootstrap-промпт из файла
   `FIRST_CODEX_MESSAGE.md`.

Не добавляй старые версии Этапов 2, 2.1, 3.3 и первоначальные аудиты.
