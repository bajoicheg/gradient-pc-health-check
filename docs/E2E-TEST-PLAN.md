# Gradient PC Health Check — E2E test plan

Цель: подтвердить на реальном корпоративном Windows 11 ПК поведение финального single-file EXE, диагностики без elevation, trusted-path policy, UAC под отдельной Service Desk admin-учёткой, безопасной `CleanTemp` remediation и отчёта до/после.

## 0. Что не делать в первом прогоне

Первый E2E выполняется **без DISM/SFC**. Они остаются доступными инженеру, но для проверки основного workflow достаточно `CleanTemp`.

Не отключать AV/EDR, WMI, службы Windows и политики. Не менять системное время. Не запускать неизвестные helper-скрипты от администратора.

Начиная с 0.3.4 **не подтверждать UAC для EXE, запущенного из Downloads**. Административные remediation разрешены только из канонического Program Files path.

## 1. Исходные условия

- Windows 11 x64, желательно типовой корпоративный образ.
- Тест выполняется из обычной пользовательской сессии.
- Есть отдельная административная учётная запись Service Desk для UAC.
- Тестируемый `Gradient-PC-Health-Check.exe` и файл `.sha256` получены из одного зелёного GitHub Actions artifact.
- Известен SHA-256 тестируемого EXE.

## 2. Подготовить детерминированный CleanTemp-сценарий

Из корня репозитория:

```powershell
powershell.exe -NoLogo -NoProfile -File .\tools\e2e\Prepare-CleanTempScenario.ps1
```

Расширенная проверка reparse point/junction:

```powershell
powershell.exe -NoLogo -NoProfile -File .\tools\e2e\Prepare-CleanTempScenario.ps1 -IncludeJunctionTest
```

Скрипт работает только со специально выделенными E2E-файлами. Он **не запускает remediation**.

Будут созданы:

- `%TEMP%\GradientPcHealthCheck-E2E\old-delete-me.txt` — файл старше 5 дней, должен быть удалён `CleanTemp`;
- `%TEMP%\GradientPcHealthCheck-E2E\fresh-must-stay.txt` — свежий файл, должен сохраниться без изменений;
- `e2e-manifest.json` с SHA-256 и ожидаемым результатом;
- опционально junction на безопасный внешний sentinel, который remediation обязана пропустить.

Важно: сама `CleanTemp` в приложении очищает реальные старые Temp-файлы пользователя и Windows согласно своей штатной логике. E2E-sentinel только делает результат этой логики измеримым.

## 3. Диагностический запуск из Downloads без elevation

1. Запустить EXE обычным двойным кликом из Downloads, **не** `Run as administrator`.
2. Убедиться, что GUI открылся без UAC.
3. Дождаться завершения диагностики.
4. Зафиксировать:
   - Health Score и общий статус;
   - Coverage и `MissingSignals`;
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

## 4. Trusted-path gate: из Downloads UAC не должен запускаться

Оставив GUI запущенным из Downloads:

1. Выбрать `CleanTemp`.
2. Нажать **Применить выбранное**.

### Acceptance

- UAC **не появляется**;
- remediation не выполняется;
- приложение сообщает, что административные действия разрешены только для копии в `%ProgramFiles%\Gradient\PCHealthCheck`;
- `old-delete-me.txt` остаётся на месте;
- GUI не падает и позволяет продолжить работу.

Это отдельный security acceptance test 0.3.4.

## 5. Разместить проверенный EXE в каноническом Program Files path

Для реального корпоративного пилота ожидается централизованное software deployment. Для разового E2E допускается контролируемое ручное размещение тестового артефакта инженером.

До копирования сверить SHA-256 с checksum из того же зелёного CI artifact:

```powershell
Get-FileHash "C:\Path\To\Gradient-PC-Health-Check.exe" -Algorithm SHA256
Get-Content "C:\Path\To\Gradient-PC-Health-Check.exe.sha256"
```

После этого инженер с административными правами размещает **именно этот проверенный файл** по пути:

`%ProgramFiles%\Gradient\PCHealthCheck\Gradient-PC-Health-Check.exe`

После копирования снова проверить SHA-256 установленной копии и сравнить с CI checksum.

### Acceptance

- исходный, CI checksum и Program Files copy имеют один SHA-256;
- `Gradient` и `PCHealthCheck` — обычные каталоги, не junction/symlink/reparse point.

## 6. Запуск канонической копии как standard user

1. Из обычной пользовательской сессии запустить `%ProgramFiles%\Gradient\PCHealthCheck\Gradient-PC-Health-Check.exe` обычным двойным кликом.
2. Убедиться, что первичная диагностика снова выполняется **без UAC**.
3. Выбрать только `CleanTemp`.
4. Нажать **Применить выбранное**.

## 7. UAC cancel

В первом UAC нажать Cancel.

### Acceptance

- remediation не выполняется;
- программа показывает понятное сообщение об отмене;
- `old-delete-me.txt` остаётся на месте;
- исходный GUI не падает и позволяет повторить операцию.

После этого заново нажать **Применить выбранное**.

## 8. UAC под отдельной admin-учёткой Service Desk

1. В UAC ввести отдельные административные credentials.
2. Дождаться завершения `CleanTemp` и автоматической повторной диагностики.
3. Проверить вкладку **До / после**.

### Acceptance

- standard-user GUI получает результат от elevated worker через named pipe;
- elevated worker запускается именно из канонического Program Files path;
- нет второго GUI или зависшего worker;
- remediation завершается и запускается повторный scan;
- результат `CleanTemp` присутствует во вкладке remediation и в verification report.

## 9. Автоматическая проверка CleanTemp

```powershell
powershell.exe -NoLogo -NoProfile -File .\tools\e2e\Verify-CleanTempScenario.ps1
```

Ожидается:

- `PASS Old Temp sentinel deleted`;
- `PASS Fresh Temp sentinel preserved`;
- если включён junction test: `PASS Reparse/junction target preserved`.

Exit code `0` означает полный PASS, `10` — хотя бы одна проверка провалена.

## 10. Сверить SHA-256 установленной копии ещё раз

```powershell
Get-FileHash "C:\Path\To\Gradient-PC-Health-Check.exe" -Algorithm SHA256
Get-FileHash "$env:ProgramFiles\Gradient\PCHealthCheck\Gradient-PC-Health-Check.exe" -Algorithm SHA256
```

### Acceptance

Хэши совпадают с CI checksum и друг с другом.

## 11. Собрать evidence одним ZIP

```powershell
powershell.exe -NoLogo -NoProfile -File .\tools\e2e\Collect-E2EEvidence.ps1 -SourceExe "C:\Path\To\Gradient-PC-Health-Check.exe"
```

Скрипт собирает:

- OS/build и базовые machine metadata;
- SHA-256 и FileVersion исходного и установленного EXE;
- результат сравнения хэшей;
- E2E manifest и verification;
- несколько последних HTML/JSON/ZIP отчётов PC Health Check.

ZIP создаётся на Desktop. Он может содержать имя ПК/пользователя и диагностические данные, поэтому рассматривается как внутренний Service Desk evidence.

## 12. Очистить E2E test data

После сохранения evidence:

```powershell
powershell.exe -NoLogo -NoProfile -File .\tools\e2e\Cleanup-E2EScenario.ps1
```

Cleanup удаляет только фиксированные E2E paths.

## 13. Что прислать разработчику после E2E

Достаточно одного ZIP из `Collect-E2EEvidence.ps1` и, если GUI выглядел неправильно, 1–2 скриншотов.

Особенно важны любые случаи:

- coverage ниже ожидаемого;
- Kaspersky/KEDR не определяется;
- физический диск имеет `Unknown` при доступной Windows Storage telemetry;
- CPU/RAM/queue заметно отличаются от Task Manager/Resource Monitor;
- из Downloads всё-таки появился UAC на административное действие;
- canonical Program Files copy ошибочно считается недоверенной;
- UAC под отдельной admin-учёткой не может подключиться к pipe;
- SHA-256 Program Files copy не совпадает с CI artifact;
- fresh sentinel удалён;
- junction target затронут;
- verification report не соответствует фактическому состоянию.

## 14. Gate перед следующим этапом

Расширять remediation или считать приложение готовым к пилотному развёртыванию следует только после PASS следующих пунктов:

- standard-user scan из Downloads;
- **trusted-path gate без UAC из Downloads**;
- canonical Program Files deployment + SHA match;
- standard-user scan из Program Files;
- UAC cancel;
- separate-admin UAC;
- CleanTemp old/fresh preservation test;
- optional reparse/junction preservation test;
- re-scan / before-after report;
- приемлемый coverage на корпоративном образе;
- отсутствие критичных false positive по Health Score.
