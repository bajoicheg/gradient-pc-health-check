# Security design

## Принципы

1. Диагностика не модифицирует ОС.
2. GUI не требует elevation для обычного сканирования.
3. Административные права запрашиваются только после явного выбора инженером действия.
4. Elevated worker — **тот же EXE**, а не PowerShell/BAT/helper из внешнего каталога.
5. Worker принимает только hardcoded allow-list: `CleanTemp`, `FlushDns`, `Dism`, `Sfc`.
6. Worker не принимает произвольную команду, executable path, каталог удаления или output path.
7. Для `CleanTemp` профиль интерактивного пользователя определяется самой программой через Windows, а не передаётся непривилегированным caller.
8. При обходе Temp reparse points/junction/symlink игнорируются; сам корень Temp с атрибутом reparse point не обрабатывается.
9. Каталоги Temp целиком не удаляются: удаляются только обычные файлы старше порога.
10. После remediation обязательно выполняется новая диагностика; факт успешного запуска команды не считается подтверждением устранения причины.
11. Если GUI уже запущен elevated, новый UAC не создаётся: разрешённые действия выполняются в том же процессе.
12. Если GUI работает standard user и требуется UAC, EXE должен находиться под `%ProgramFiles%`. Запрос elevation из Downloads/Temp/пользовательского профиля блокируется.

## UAC и IPC

Для результата elevated worker **не пишет privileged-файл в пользовательский Temp**. Это исключает класс атак с подменой каталога/junction для privileged file write.

Перед запуском worker GUI:

- генерирует случайный GUID session id;
- создаёт локальный named pipe `GradientPcHealthCheck-<GUID>`;
- генерирует 256-битный случайный nonce;
- запускает тот же EXE через `runas`, передавая только session id, allow-listed action IDs, ограниченный Temp age, pipe name и nonce.

Worker:

- проверяет формат session/pipe/nonce;
- повторно фильтрует список действий по hardcoded allow-list;
- требует административный токен для `CleanTemp`, `Dism`, `Sfc`;
- возвращает JSON только через созданный parent-процессом named pipe;
- возвращает nonce и session id, которые parent проверяет перед принятием результата.

Named pipe не используется для передачи произвольных команд worker-у: privileged action IDs уже находятся в command line и затем повторно валидируются самим worker.

## Осознанно исключено

- `netsh winsock reset` как универсальная remediation;
- очистка `SoftwareDistribution`;
- очистка Prefetch;
- автоматический reboot;
- kill процессов;
- отключение startup/services;
- произвольное исполнение команд;
- загрузка кода/правил remediation из сети;
- автоматическое применение DISM/SFC без явной галочки инженера.

## Развёртывание

Для режима auto-elevation рекомендуемый путь:

`%ProgramFiles%\Gradient\PCHealthCheck\Gradient-PC-Health-Check.exe`

Развёртывание желательно выполнять централизованно (ПО-развёртывание/Intune/SCCM/Kaspersky и т. п.) с контролем SHA-256 или Authenticode.

Диагностический режим может запускаться и из другого каталога, но auto-elevation из user-writable location намеренно блокируется. Инженер при необходимости может сначала запустить проверенную копию EXE штатным `Run as administrator`.

## Подпись

CI формирует SHA-256. Authenticode в текущей версии не выполняется, так как сертификат подписи в репозитории отсутствует. Перед широким корпоративным развёртыванием рекомендуется подписывать release EXE корпоративным code-signing сертификатом и проверять publisher средствами AppLocker/WDAC/EDR.
