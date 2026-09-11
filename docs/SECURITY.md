# Security design

## Принципы

1. Диагностика не модифицирует ОС и не требует elevation.
2. Любая remediation выполняется только после явного выбора инженером и подтверждения списка действий.
3. `CleanTemp` не является privileged remediation: он работает только с `%LOCALAPPDATA%\Temp` интерактивного пользователя и выполняется только без admin token.
4. `CleanTemp` не очищает `%WINDIR%\Temp`, не удаляет каталоги целиком, не проходит через reparse points/junction/symlink и не принимается elevated worker-ом.
5. Elevated worker — тот же EXE, а не PowerShell/BAT/helper из внешнего каталога.
6. Worker разрешён только из точного канонического пути `%ProgramFiles%\G\PCHealthCheck\G-PC-Health-Check.exe`.
7. Worker принимает только hardcoded allow-list `FlushDns`, `Dism`, `Sfc`; `CleanTemp`, unknown и mixed known/unknown списки отклоняются до подключения к IPC.
8. `Dism` и `Sfc` требуют administrative token. `FlushDns` обычно выполняется локально без elevation; при совместном запуске с privileged action может входить в тот же worker batch.
9. Worker не принимает произвольную команду, executable path, каталог удаления или output path.
10. После remediation выполняется новая диагностика; успешный exit code сам по себе не считается подтверждением устранения причины.
11. Bootstrap/elevation из Downloads или другого user-writable пути отключён.

## Граница CleanTemp

До 0.3.5 `CleanTemp` выполнялся в elevated worker и рекурсивно обходил пользовательский Temp и `%WINDIR%\Temp`. Даже при проверке reparse-point атрибутов такая схема оставляла TOCTOU-границу: непривилегированный пользователь может изменять собственное дерево каталогов во время обхода privileged процессом.

Начиная с 0.3.5 модель изменена:

- `CleanTemp` выполняется исходным standard-user процессом;
- область действия — только `%LOCALAPPDATA%\Temp` текущего интерактивного пользователя;
- elevated worker не содержит `CleanTemp` в своей allow-list;
- если приложение вручную запущено elevated, `CleanTemp` fail closed и просит перезапустить приложение обычным пользователем;
- если вместе выбраны `CleanTemp` и `Dism`/`Sfc`, сначала завершается privileged worker, затем `CleanTemp` выполняется исходным непривилегированным parent-процессом;
- reparse points всё равно пропускаются как defense in depth.

Таким образом user-controlled directory tree больше не обходится с административным токеном.

## Trusted path и UAC

Для `Dism`/`Sfc` GUI до показа UAC проверяет собственный путь. Разрешён только:

`%ProgramFiles%\G\PCHealthCheck\G-PC-Health-Check.exe`

Проверяются:

- точное совпадение нормализованного пути;
- отсутствие reparse-point у EXE;
- отсутствие reparse-point у существующих каталогов `G` и `PCHealthCheck` в цепочке.

Из Downloads диагностика, отчёты, `CleanTemp` и `FlushDns` разрешены без elevation. При выборе `Dism`/`Sfc` из Downloads операция отклоняется **до** запуска UAC.

## IPC elevated worker

Перед запуском worker GUI:

- генерирует случайный GUID session id;
- создаёт локальный named pipe `GPcHealthCheck-<GUID>`;
- генерирует 256-битный случайный nonce;
- передаёт только session id, allow-listed action IDs, pipe name и nonce;
- запускает тот же канонический EXE через `runas`.

Worker:

- первым делом проверяет собственный canonical path;
- валидирует session/pipe/nonce;
- целиком отклоняет список, содержащий `CleanTemp` или неизвестное действие;
- требует administrative token для `Dism`/`Sfc`;
- подключается к named pipe до запуска долгих действий;
- возвращает JSON только через pipe.

Parent принимает результат только при совпадении одноразовых `session id + nonce`.

### UAC под отдельной admin-учёткой

Service Desk может ввести в UAC отдельную локальную или доменную admin-учётку. Поэтому DACL named pipe разрешает подключение SID текущего GUI-пользователя и локальной группе `Administrators`. Это разрешение относится только к каналу результата; команды worker получает из собственной командной строки и повторно валидирует по hardcoded allow-list.

## Осознанно исключено

- `netsh winsock reset` как универсальная remediation;
- очистка `SoftwareDistribution`;
- очистка Prefetch;
- очистка `%WINDIR%\Temp`;
- автоматический reboot;
- kill процессов;
- отключение startup/services;
- произвольное исполнение команд;
- загрузка правил remediation из сети;
- автоматическое применение DISM/SFC без явной галочки инженера;
- elevation/bootstrap из user-writable каталога.

## Развёртывание

Канонический trusted path для administrative remediation:

`%ProgramFiles%\G\PCHealthCheck\G-PC-Health-Check.exe`

Централизованное развёртывание через корпоративный software deployment — ожидаемый вариант для пилота. Диагностика и непривилегированные действия могут запускаться из другого пути.

## Supply chain и CI

GitHub Actions:

- pins сторонние Actions на immutable commit SHA;
- build workflow имеет только `contents: read` и не сохраняет checkout credentials;
- фиксирует .NET SDK;
- проверяет NuGet vulnerabilities, включая transitive packages;
- собирает с warnings-as-errors;
- запускает source self-test и self-test опубликованного single-file EXE;
- проверяет отказ worker из untrusted path;
- проверяет отказ elevated worker для `CleanTemp`, unknown и mixed action list, invalid pipe и invalid nonce;
- проверяет точную FileVersion и SHA-256 артефакта;
- генерирует брендовые assets из детерминированного source и проверяет SHA-256 корпоративного shield;
- проверяет `START-HERE.txt` строгим UTF-8 decoder-ом.

### GitHub Release

Private repository сейчас не использует защищённый `main`, поэтому автоматическая публикация release после любого зелёного push отключена. `Publish GitHub Release` запускается вручную и принимает `run_id` уже завершённого `Windows EXE` workflow.

Перед публикацией workflow проверяет, что указанный run:

- действительно `Windows EXE`;
- завершён успешно;
- был событием `push`;
- выполнялся на `main`;
- имеет валидный 40-символьный commit SHA.

Затем checkout выполняется на точный tested SHA, артефакты скачиваются именно из указанного run, SHA-256 и FileVersion проверяются повторно, а GitHub Release создаётся с target на этот же SHA.

После появления branch protection для `main` можно вернуть автоматический release trigger.

## Подпись

CI формирует SHA-256. Authenticode в 0.3.5 не выполняется, так как code-signing certificate отсутствует в release pipeline. Перед широким корпоративным развёртыванием рекомендуется подписывать EXE корпоративным code-signing сертификатом и проверять publisher средствами AppLocker/WDAC/EDR.
