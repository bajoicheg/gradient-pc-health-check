# Gradient PC Health Check

Внутреннее Windows-приложение Service Desk для первичной диагностики жалоб **«компьютер сильно тормозит»** и безопасного применения выбранных remediation.

Текущая версия разработки: **0.3.5 pilot**.

Для инженера это один GUI-файл `Gradient-PC-Health-Check.exe`. Диагностика запускается автоматически; рекомендации отображаются с галочками только там, где действие разрешено автоматизировать. По кнопке **«Применить выбранное»** приложение выполняет подтверждённый allow-list действий и затем автоматически повторяет диагностику с отчётом **до/после**.

## Что изменено в 0.3.5

- `CleanTemp` всегда виден Service Desk. При недостатке места он `Рекомендуется` и предвыбран; при нормальном запасе — `Дополнительно` и выключен по умолчанию.
- `CleanTemp` теперь очищает только старые обычные файлы из `%LOCALAPPDATA%\Temp` текущего пользователя и выполняется **без elevation**. Elevated worker это действие не принимает.
- Кнопка **«Применить выбранное»** неактивна, пока не отмечено хотя бы одно автоматизируемое действие, и показывает число выбранных действий.
- Ручные рекомендации нельзя ошибочно «применить».
- TOP CPU / RAM / I/O используют типизированные числовые значения и сортируются как числа; `%`, `MB`, `MB/s` — только формат отображения.
- Event ID и Count сортируются как числа, timestamp — как `DateTime`; отсутствующее время отображается пустым.
- `START-HERE.txt` записывается UTF-8 BOM и проверяется строгим UTF-8 decoder-ом.
- Финальный GUI использует детерминированно восстановленный корпоративный G-shield; зелёная иконка приложения генерируется из того же source во время build.
- GitHub Release публикуется вручную только из `run_id` успешного `Windows EXE` push-run на `main`.

## Coverage: отсутствие данных не выглядит как «всё хорошо»

Health Check отдельно рассчитывает полноту диагностической телеметрии. В coverage входят CPU, RAM, системный диск, очередь диска, health физического накопителя, Windows/build, TOP процессов и антивирус/EDR.

Если важные источники недоступны, приложение перечисляет отсутствующие сигналы, добавляет WARN категории `Данные`, не показывает общий `OK` при недостаточном coverage и сохраняет `CoveragePercent`, `CoverageStatus` и `MissingSignals` в JSON-отчёте.

## Windows Event Log

Абсолютное количество `Critical/Error` само по себе не является достаточным сигналом. WARN/CRIT требует повторяемости одного `Provider/Event ID`, что снижает ложные срабатывания на разнородный фоновый шум.

## Измерение производительности

CPU и дисковая очередь оцениваются по серии из 5 замеров с использованием медианы. TOP процессов по CPU/RAM/I/O снимается отдельным интервальным замером.

## Security boundary

### Непривилегированные действия

Из Downloads или любого обычного рабочего каталога без UAC разрешены:

- диагностика и формирование отчётов;
- `CleanTemp` — только `%LOCALAPPDATA%\Temp` текущего пользователя;
- `FlushDns`.

`CleanTemp` не очищает `%WINDIR%\Temp`, Prefetch и другие системные каталоги. Reparse points/junction/symlink пропускаются. Если приложение вручную запущено через **Run as administrator**, `CleanTemp` fail closed, чтобы user-controlled directory tree не обходился с admin token.

### Administrative remediation

`DISM RestoreHealth` и `SFC /scannow` требуют UAC и разрешены только если EXE находится по точному пути:

`%ProgramFiles%\Gradient\PCHealthCheck\Gradient-PC-Health-Check.exe`

Автоматический bootstrap из Downloads в Program Files отключён. Перед privileged remediation EXE должен быть размещён доверенным corporate software deployment либо контролируемо установлен после сверки SHA-256.

При UAC допускается отдельная admin-учётка Service Desk. Named pipe разрешает подключение local Administrators, а одноразовые `session ID + nonce` связывают ответ с конкретным GUI request.

Elevated worker дополнительно fail closed:

- не запускается из произвольного пути;
- не доверяет reparse-point на canonical path;
- принимает только `FlushDns`, `Dism`, `Sfc`;
- **не принимает `CleanTemp`**;
- отклоняет mixed action list с неизвестным действием;
- legacy `--bootstrap-worker` всегда возвращает отказ.

Подробности: `docs/SECURITY.md`.

## Разрешённые автоматические действия

- очистка старых файлов пользовательского Temp — без elevation;
- Flush DNS — по решению инженера;
- DISM RestoreHealth — по решению инженера и через UAC;
- SFC /scannow — по решению инженера и через UAC.

Prefetch, Windows Update cache, Winsock reset, принудительная перезагрузка, kill процессов и отключение автозагрузки автоматически не выполняются.

## Сборка и контроль

Windows 11 x64, .NET 8 WinForms, self-contained single-file EXE.

`Windows EXE` workflow должен пройти:

- PowerShell parser/smoke gates;
- deterministic brand asset generation и SHA-256 check корпоративного shield;
- NuGet vulnerability audit, включая transitive dependencies;
- build с warnings-as-errors;
- source и published-EXE self-tests;
- worker security negative tests, включая запрет `CleanTemp` в elevated worker;
- точную проверку FileVersion;
- SHA-256 финального EXE;
- создание и строгую UTF-8 проверку pilot bundle.

## GitHub Release

Поскольку `main` private repository пока не защищён branch protection, автоматическая публикация после любого зелёного push отключена.

Workflow `Publish GitHub Release` запускается вручную с `run_id`. Он принимает только успешный `Windows EXE` run, созданный событием `push` на `main`, checkout-ит точный tested SHA, скачивает артефакты именно этого run, повторно проверяет SHA-256/FileVersion и создаёт release с target на тот же SHA.

После появления branch protection для `main` автоматический trigger можно вернуть.

## Подпись

0.3.5 публикует SHA-256, но пока не использует Authenticode. Перед широким корпоративным развёртыванием рекомендуется добавить code signing и контроль publisher через AppLocker/WDAC/EDR.
