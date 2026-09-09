# Security design

## Принципы

1. Диагностика не модифицирует ОС.
2. GUI не требует elevation для обычного сканирования.
3. Административные права запрашиваются только после явного выбора инженером действия.
4. Elevated worker — **тот же EXE**, а не PowerShell/BAT/helper из внешнего каталога.
5. Privileged worker разрешён только из точного канонического пути `%ProgramFiles%\Gradient\PCHealthCheck\Gradient-PC-Health-Check.exe`.
6. Worker принимает только hardcoded allow-list: `CleanTemp`, `FlushDns`, `Dism`, `Sfc`; mixed known/unknown list отклоняется целиком.
7. Worker не принимает произвольную команду, executable path, каталог удаления или output path.
8. Для `CleanTemp` профиль интерактивного пользователя определяется самой программой через Windows, а не передаётся непривилегированным caller.
9. При обходе Temp reparse points/junction/symlink игнорируются; сам корень Temp с атрибутом reparse point не обрабатывается.
10. Каталоги Temp целиком не удаляются: удаляются только обычные файлы старше порога.
11. После remediation обязательно выполняется новая диагностика; факт успешного запуска команды не считается подтверждением устранения причины.
12. Автоматический bootstrap из Downloads/user-writable path отключён до появления Authenticode или другого доверенного механизма доставки.

## Почему bootstrap из Downloads отключён

В 0.3.1–0.3.3 существовал сценарий: standard-user GUI из Downloads запрашивает UAC, elevated bootstrap копирует тот же EXE в Program Files и сверяет SHA-256. Для пилота этот сценарий признан недостаточно сильной границей доверия.

Причина — pre-UAC race: user-writable путь остаётся изменяемым до момента запуска elevated child. SHA-256, вычисленный уже после elevation, подтверждает лишь то, что **скопированный файл совпадает с файлом, прочитанным elevated bootstrap**, но сам по себе не доказывает, что это именно тот неизменённый бинарник, которому инженер намеревался дать administrative token.

Поэтому начиная с 0.3.4:

- диагностику разрешено запускать из Downloads без admin token;
- `CleanTemp`, `Dism`, `Sfc` из user-writable location не запускаются и UAC даже не инициируется;
- `--bootstrap-worker` fail closed и возвращает отказ;
- privileged `--worker` отказывается работать вне канонического Program Files path;
- arbitrary path внутри Program Files также не считается доверенным;
- на каноническом пути проверяются reparse-point признаки файла и существующих каталогов в цепочке.

Перед административной remediation приложение должно быть размещено в Program Files через управляемый корпоративный software deployment или иной доверенный канал.

## UAC и IPC

Перед запуском worker GUI:

- проверяет, что текущий EXE находится по каноническому trusted path;
- генерирует случайный GUID session id;
- создаёт локальный named pipe `GradientPcHealthCheck-<GUID>`;
- генерирует 256-битный случайный nonce;
- передаёт только session id, allow-listed action IDs, ограниченный Temp age, pipe name и nonce;
- запускает тот же канонический EXE через `runas` в worker-mode.

Worker:

- первым делом проверяет собственный путь выполнения;
- проверяет формат session/pipe/nonce;
- fail closed валидирует полный список действий по hardcoded allow-list;
- требует административный токен для `CleanTemp`, `Dism`, `Sfc`;
- подключается к named pipe **до** запуска долгих действий, чтобы GUI быстро мог подтвердить успешное elevation;
- возвращает JSON только через созданный parent-процессом named pipe;
- возвращает nonce и session id, которые parent проверяет перед принятием результата.

### UAC под отдельной admin-учёткой

Service Desk может ввести в UAC не текущую пользовательскую, а отдельную административную учётку. Поэтому DACL named pipe разрешает доступ:

- SID текущего GUI-пользователя;
- локальной группе `Administrators`.

Это нужно только для IPC. Результат всё равно принимается GUI только при совпадении одноразовых `session id + nonce`. Named pipe не используется для передачи произвольных команд worker-у: privileged action IDs уже находятся в command line и повторно валидируются самим worker.

## Осознанно исключено

- `netsh winsock reset` как универсальная remediation;
- очистка `SoftwareDistribution`;
- очистка Prefetch;
- автоматический reboot;
- kill процессов;
- отключение startup/services;
- произвольное исполнение команд;
- загрузка кода/правил remediation из сети;
- автоматическое применение DISM/SFC без явной галочки инженера;
- elevation/bootstrap из user-writable каталога.

## Развёртывание

Единственный trusted remediation path:

`%ProgramFiles%\Gradient\PCHealthCheck\Gradient-PC-Health-Check.exe`

Централизованное развёртывание (Kaspersky/Intune/SCCM/другой корпоративный software deployment) — предпочтительный и ожидаемый вариант для пилота. Диагностический scan можно запускать из другого пути, но административные remediation там заблокированы.

## Supply chain и CI

GitHub Actions:

- pins сторонние Actions на immutable commit SHA;
- не сохраняет checkout credentials;
- фиксирует .NET SDK;
- проверяет NuGet vulnerabilities, включая transitive packages;
- собирает с warnings-as-errors;
- запускает source self-test и self-test опубликованного single-file EXE;
- проверяет отказ worker из untrusted path;
- размещает временную CI-копию ровно по каноническому Program Files path и уже там проверяет negative worker cases;
- проверяет unknown и mixed known/unknown action list, invalid pipe, invalid nonce;
- сверяет FileVersion и SHA-256 артефакта.

## Подпись

CI формирует SHA-256. Authenticode в текущей версии не выполняется, так как сертификат подписи в репозитории отсутствует. Перед широким корпоративным развёртыванием рекомендуется подписывать release EXE корпоративным code-signing сертификатом и проверять publisher средствами AppLocker/WDAC/EDR.
