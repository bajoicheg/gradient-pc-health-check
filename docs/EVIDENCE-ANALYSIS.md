# E2E evidence analysis

`Analyze-E2EEvidence.ps1` предназначен для быстрой автоматической первичной проверки ZIP, сформированного `Collect-E2EEvidence.ps1` после реального E2E.

Он не заменяет инженерный разбор диагностических данных. Его задача — отделить **корректность E2E-процедуры и evidence** от фактического состояния тестового ПК.

## Запуск

```powershell
powershell.exe -NoLogo -NoProfile -File .\tools\e2e\Analyze-E2EEvidence.ps1 -EvidenceZip "C:\Path\Gradient-PC-Health-Check-E2E-HOST-20260909_230000.zip"
```

Также можно анализировать уже распакованный bundle:

```powershell
powershell.exe -NoLogo -NoProfile -File .\tools\e2e\Analyze-E2EEvidence.ps1 -EvidenceDirectory "C:\Path\evidence"
```

По умолчанию рядом с evidence создаются:

- `Gradient-PC-Health-Check-E2E-analysis-*.json` — машинно-читаемый результат;
- `Gradient-PC-Health-Check-E2E-analysis-*.md` — краткий отчёт для человека.

## Вердикты и exit codes

| Вердикт | Exit code | Значение |
|---|---:|---|
| `PASS` | 0 | Evidence полон, основные acceptance-проверки прошли |
| `WARN` | 10 | E2E не доказан как сломанный, но есть неполная телеметрия/предупреждения, требующие разбора |
| `FAIL` | 20 | Есть провал acceptance test, противоречие или отсутствует обязательный evidence |

## Что проверяется

Анализатор проверяет:

- структуру evidence и поддерживаемую schema version;
- что тест выполнялся на Windows 11 либо явно помечает отклонение;
- наличие и совпадение SHA-256 исходного и Program Files EXE;
- наличие `e2e-manifest.json`;
- успешность `e2e-verification.json`;
- наличие application verification JSON с объектами `Before`, `After`, `Remediation`;
- совпадение имени ПК в `Before` и `After`;
- diagnostic coverage после remediation: `>=85%` PASS, `65–84%` WARN, `<65%` FAIL;
- collection warnings;
- наличие и `Success=true` для `CleanTemp`;
- наличие optional junction scenario, если он был включён.

Перед распаковкой ZIP имена entries валидируются. Абсолютные пути и `..` отклоняются, чтобы анализатор сам не создавал ZIP traversal риск.

## Что намеренно НЕ считается E2E failure

`Health Score` и общий `OK/WARN/CRIT` после remediation **не определяют PASS/FAIL теста**.

Например, ПК с проблемным SSD может корректно получить `CRIT` и после успешного `CleanTemp`. Это хороший результат диагностики, а не провал E2E. Анализатор сохраняет score/status как информационные показатели и отдельно оценивает, правильно ли отработали workflow, coverage, remediation и evidence.

## Что делать после анализа

При `PASS` ZIP всё равно нужно просмотреть на предмет корректности конкретных диагностических выводов и false positive.

При `WARN` в первую очередь смотреть `MissingSignals`, `CollectionWarnings`, Kaspersky/KEDR detection, physical disk telemetry и соответствие CPU/RAM/disk queue Task Manager/Resource Monitor.

При `FAIL` сначала исправлять конкретный провал E2E или сбор evidence, а не калибровать Health Score.

## CI

Для самого анализатора есть отдельный Windows CI gate. Он проверяет:

- PowerShell syntax;
- synthetic полноценный PASS bundle;
- synthetic неполный bundle, который обязан вернуть `FAIL/20`;
- отказ от ZIP с traversal entry `../escape.txt`.

Таким образом, когда появится первый реальный ZIP, базовая логика его обработки уже проверена независимо от корпоративного ПК.
