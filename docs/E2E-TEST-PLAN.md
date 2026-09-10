# Gradient PC Health Check 0.3.5 — E2E test plan

Цель: подтвердить на реальном корпоративном Windows 11 ПК финальный single-file EXE, работу Service Desk UX, непривилегированный `CleanTemp`, trusted-path/UAC boundary для DISM/SFC и корректность evidence.

## 0. Базовый принцип теста

Первый пилотный прогон не требует фактического запуска DISM/SFC. Для проверки основной пользовательской цепочки достаточно диагностики и `CleanTemp`. Проверка отказа privileged action из Downloads выполняется без запуска самой команды и без UAC.

Положительный тест UAC под отдельной admin-учёткой с фактическим SFC/DISM выполняйте отдельно на согласованном тестовом ПК.

Не отключайте AV/EDR, WMI, службы Windows и политики. Не запускайте неизвестные helper-скрипты от администратора.

## 1. Исходные условия

- Windows 11 x64, желательно типовой корпоративный образ;
- обычная пользовательская сессия;
- тестовый `Gradient-PC-Health-Check.exe` и `.sha256` из одного зелёного `Windows EXE` run;
- известен SHA-256 артефакта;
- для отдельного privileged E2E доступна Service Desk admin-учётка.

## 2. Подготовить CleanTemp-сценарий

Из pilot bundle:

```powershell
powershell.exe -NoLogo -NoProfile -File .\e2e\Prepare-CleanTempScenario.ps1
```

Расширенная проверка reparse point/junction:

```powershell
powershell.exe -NoLogo -NoProfile -File .\e2e\Prepare-CleanTempScenario.ps1 -IncludeJunctionTest
```

Создаются только специальные E2E-файлы внутри пользовательского `%TEMP%`:

- `old-delete-me.txt` — старше порога, должен быть удалён;
- `fresh-must-stay.txt` — свежий, должен сохраниться;
- `e2e-manifest.json`;
- опционально junction на безопасный внешний sentinel, который должен сохраниться.

## 3. Диагностический запуск из Downloads без elevation

1. Запустите EXE обычным двойным кликом из Downloads или распакованного pilot-каталога.
2. Убедитесь, что UAC не появляется.
3. Дождитесь диагностики.
4. Проверьте Health Score, Coverage, CPU/RAM, диск, uptime, TOP CPU/RAM/I/O, физический диск, security product и Windows events.
5. Откройте HTML-отчёт и визуально сравните данные с GUI.

### Acceptance

- диагностика работает без admin token;
- GUI остаётся отзывчивым;
- отсутствующая телеметрия не выглядит как полностью здоровый результат;
- отчёт создаётся в `%LOCALAPPDATA%\Gradient\PCHealthCheck\Reports`.

## 4. CleanTemp из Downloads — без UAC

1. Выберите только `Очистить старые временные файлы пользователя`.
2. Нажмите **Применить выбранное**.
3. Подтвердите список действий.

### Acceptance

- UAC не появляется;
- выполняется только очистка `%LOCALAPPDATA%\Temp` текущего пользователя;
- `%WINDIR%\Temp` не является областью действия;
- после операции автоматически выполняется повторная диагностика;
- результат `CleanTemp` присутствует в отчёте `До / после`.

## 5. Автопроверка CleanTemp

```powershell
powershell.exe -NoLogo -NoProfile -File .\e2e\Verify-CleanTempScenario.ps1
```

Ожидается:

- `PASS Old Temp sentinel deleted`;
- `PASS Fresh Temp sentinel preserved`;
- при `-IncludeJunctionTest`: `PASS Reparse/junction target preserved`.

Exit code `0` означает PASS.

## 6. Trusted-path gate для privileged remediation

Оставив GUI запущенным из Downloads:

1. Снимите остальные галочки.
2. Выберите `SFC /scannow` **только для проверки gate**.
3. Нажмите **Применить выбранное** и подтвердите список.

### Acceptance

- UAC не появляется;
- SFC не запускается;
- приложение сообщает, что DISM/SFC разрешены только для проверенной копии в `%ProgramFiles%\Gradient\PCHealthCheck`;
- GUI не падает и позволяет продолжить работу.

После проверки снимите галочку SFC.

## 7. Разместить проверенный EXE в canonical Program Files path

Для corporate pilot предпочтительно централизованное software deployment. Для разового теста допускается контролируемое ручное размещение администратором.

До и после копирования сравните SHA-256:

```powershell
Get-FileHash .\Gradient-PC-Health-Check.exe -Algorithm SHA256
Get-Content .\Gradient-PC-Health-Check.exe.sha256
Get-FileHash "$env:ProgramFiles\Gradient\PCHealthCheck\Gradient-PC-Health-Check.exe" -Algorithm SHA256
```

### Acceptance

- все хэши совпадают;
- `Gradient` и `PCHealthCheck` — обычные каталоги, не junction/symlink/reparse point.

## 8. Canonical copy как standard user

1. Запустите `%ProgramFiles%\Gradient\PCHealthCheck\Gradient-PC-Health-Check.exe` обычным двойным кликом.
2. Убедитесь, что сама диагностика не требует UAC.
3. `CleanTemp` по-прежнему должен выполняться без UAC.

## 9. UAC cancel для SFC/DISM

На canonical copy:

1. Выберите только `SFC /scannow`.
2. Нажмите **Применить выбранное**.
3. В UAC нажмите Cancel.

### Acceptance

- SFC не выполняется;
- программа показывает понятное сообщение об отмене;
- исходный GUI остаётся рабочим.

## 10. Отдельный positive UAC test

Этот шаг выполняется на согласованном тестовом ПК, где допустим SFC.

1. Снова выберите только `SFC /scannow`.
2. В UAC введите отдельную Service Desk admin-учётку.
3. Дождитесь завершения и повторной диагностики.

### Acceptance

- elevated worker запускается именно из canonical Program Files path;
- standard-user GUI получает результат через named pipe;
- нет второго GUI или зависшего worker;
- exit code и результат SFC отражены во вкладке `До / после` и verification report.

DISM можно проверять отдельно только при наличии диагностического основания.

## 11. Комбинированный boundary test

На canonical copy можно выбрать одновременно `CleanTemp` и `SFC`.

Ожидаемое поведение:

- UAC относится только к SFC;
- `CleanTemp` не передаётся elevated worker-у;
- после завершения privileged worker `CleanTemp` выполняется исходным standard-user процессом;
- в итоговом batch присутствуют оба результата.

Если UAC отменён, `CleanTemp` в этом комбинированном сценарии также не должен выполняться до отмены.

## 12. Поведение при ручном Run as administrator

Запустите приложение через **Run as administrator** только как отдельный negative test и выберите `CleanTemp`.

### Acceptance

`CleanTemp` fail closed с сообщением, что очистка пользовательского Temp намеренно не выполняется из elevated-процесса. Это защищает user-controlled directory tree от обхода с admin token.

## 13. Собрать evidence

```powershell
powershell.exe -NoLogo -NoProfile -File .\e2e\Collect-E2EEvidence.ps1 -SourceExe .\Gradient-PC-Health-Check.exe
```

Evidence ZIP может содержать имя ПК/пользователя и диагностические сведения, поэтому является внутренним Service Desk artifact.

Проверьте evidence:

```powershell
powershell.exe -NoLogo -NoProfile -File .\e2e\Analyze-E2EEvidence.ps1 -EvidenceZip <path-to-zip>
```

## 14. Очистить E2E test data

```powershell
powershell.exe -NoLogo -NoProfile -File .\e2e\Cleanup-E2EScenario.ps1
```

Cleanup удаляет только фиксированные E2E paths.

## 15. Release gate

0.3.5 допускается к публикации после выполнения минимум следующих условий:

- финальный PR SHA имеет зелёные `Windows EXE` и `E2E Evidence Analyzer`;
- standard-user scan из Downloads — PASS;
- `CleanTemp` из Downloads без UAC — PASS;
- old/fresh/junction CleanTemp checks — PASS;
- SFC/DISM из Downloads блокируются до UAC — PASS;
- canonical Program Files copy имеет SHA-256, совпадающий с CI artifact;
- UAC cancel — PASS;
- для расширенного пилота: separate-admin positive UAC test — PASS;
- GitHub Release публикуется вручную только из `run_id` успешного `Windows EXE` push-run на `main`.
