# Gradient PC Health Check — E2E test plan

Цель: подтвердить на реальном корпоративном Windows 11 ПК поведение финального single-file EXE, UAC/bootstrap, безопасной remediation и отчёта до/после.

## 0. Что не делать в первом прогоне

Первый E2E выполняется **без DISM/SFC**. Они остаются доступными инженеру, но для проверки основного workflow достаточно `CleanTemp`.

Не отключать AV/EDR, WMI, службы Windows и политики. Не менять системное время. Не запускать неизвестные helper-скрипты от администратора.

## 1. Исходные условия

- Windows 11 x64, желательно типовой корпоративный образ.
- Тест выполняется из обычной пользовательской сессии.
- Есть отдельная административная учётная запись Service Desk для UAC.
- Тестируемый `Gradient-PC-Health-Check.exe` скачан локально, например в Downloads.
- Рекомендуется заранее записать SHA-256 тестируемого EXE.

## 2. Подготовить детерминированный CleanTemp-сценарий

Из корня репозитория:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\e2e\Prepare-CleanTempScenario.ps1
```

Расширенная проверка reparse point/junction:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\e2e\Prepare-CleanTempScenario.ps1 -IncludeJunctionTest
```

Скрипт работает только со специально выделенными E2E-файлами. Он **не запускает remediation**.

Будут созданы:

- `%TEMP%\GradientPcHealthCheck-E2E\old-delete-me.txt` — файл старше 5 дней, должен быть удалён `CleanTemp`;
- `%TEMP%\GradientPcHealthCheck-E2E\fresh-must-stay.txt` — свежий файл, должен сохраниться без изменений;
- `e2e-manifest.json` с SHA-256 и ожидаемым результатом;
- опционально junction на безопасный внешний sentinel, который remediation обязана пропустить.

Важно: сама `CleanTemp` в приложении очищает реальные старые Temp-файлы пользователя и Windows согласно своей штатной логике. E2E-sentinel только делает результат этой логики измеримым.

## 3. Диагностический запуск без elevation

1. Запустить EXE обычным двойным кликом, **не** `Run as administrator`.
2. Убедиться, что GUI открылся без UAC.
3. Дождаться завершения диагностики.
4. Зафиксировать:
   - Health Score и общий статус;
   - наличие/отсутствие WARN категории `Данные`;
   - CPU, RAM, свободное место, uptime;
   - TOP CPU/RAM/I/O;
   - физический диск и security product;
   - основные повторяющиеся Windows events.
5. Открыть HTML-отчёт и проверить, что данные визуально совпадают с GUI.

### Acceptance

- диагностика запускается без admin token;
- GUI остаётся отзывчивым;
- отсутствующая телеметрия не выглядит как полностью здоровый результат;
- нет очевидно невозможных значений CPU/RAM/диска;
- отчёт создаётся в `%LOCALAPPDATA%\Gradient\PCHealthCheck\Reports`.

## 4. UAC cancel

1. Выбрать только `CleanTemp`.
2. Нажать **Применить выбранное**.
3. В первом UAC нажать Cancel.

### Acceptance

- remediation не выполняется;
- программа показывает понятное сообщение об отмене;
- `old-delete-me.txt` остаётся на месте;
- исходный GUI не падает и позволяет повторить операцию.

После этого заново нажать **Применить выбранное**.

## 5. UAC под отдельной admin-учёткой Service Desk

1. В UAC ввести отдельные административные credentials.
2. Дождаться завершения `CleanTemp` и автоматической повторной диагностики.
3. Проверить появление:

`%ProgramFiles%\Gradient\PCHealthCheck\Gradient-PC-Health-Check.exe`

4. Проверить вкладку **До / после**.

### Acceptance

- standard-user GUI получает результат от elevated worker;
- нет второго GUI/зависшего bootstrap;
- Program Files copy создаётся;
- remediation завершается и запускается повторный scan;
- результат `CleanTemp` присутствует во вкладке remediation и в verification report.

## 6. Автоматическая проверка CleanTemp

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\e2e\Verify-CleanTempScenario.ps1
```

Ожидается:

- `PASS Old Temp sentinel deleted`;
- `PASS Fresh Temp sentinel preserved`;
- если включён junction test: `PASS Reparse/junction target preserved`.

Exit code `0` означает полный PASS, `10` — хотя бы одна проверка провалена.

## 7. SHA-256 installed copy

Если исходный EXE известен, финальный evidence collector сравнит его SHA-256 с Program Files copy автоматически.

Ручная проверка:

```powershell
Get-FileHash .\Gradient-PC-Health-Check.exe -Algorithm SHA256
Get-FileHash "$env:ProgramFiles\Gradient\PCHealthCheck\Gradient-PC-Health-Check.exe" -Algorithm SHA256
```

### Acceptance

Хэши совпадают.

## 8. Собрать evidence одним ZIP

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\e2e\Collect-E2EEvidence.ps1 -SourceExe "C:\Path\To\Gradient-PC-Health-Check.exe"
```

Скрипт собирает:

- OS/build и базовые machine metadata;
- SHA-256 и FileVersion исходного и установленного EXE;
- результат сравнения хэшей;
- E2E manifest и verification;
- несколько последних HTML/JSON/ZIP отчётов PC Health Check.

ZIP создаётся на Desktop. Он может содержать имя ПК/пользователя и диагностические данные, поэтому рассматривается как внутренний Service Desk evidence.

## 9. Что прислать разработчику после E2E

Достаточно одного ZIP из `Collect-E2EEvidence.ps1` и, если GUI выглядел неправильно, 1–2 скриншотов.

Особенно важны любые случаи:

- coverage ниже ожидаемого;
- Kaspersky/KEDR не определяется;
- физический диск имеет `Unknown` при доступной Windows Storage telemetry;
- CPU/RAM/queue заметно отличаются от Task Manager/Resource Monitor;
- UAC под отдельной admin-учёткой не может подключиться к pipe;
- Program Files SHA-256 не совпадает;
- fresh sentinel удалён;
- junction target затронут;
- verification report не соответствует фактическому состоянию.

## 10. Gate перед следующим этапом

Расширять remediation или считать приложение готовым к пилотному развёртыванию следует только после PASS следующих пунктов:

- standard-user scan;
- UAC cancel;
- separate-admin UAC;
- trusted Program Files copy + SHA match;
- CleanTemp old/fresh preservation test;
- re-scan / before-after report;
- приемлемый coverage на корпоративном образе;
- отсутствие критичных false positive по Health Score.
