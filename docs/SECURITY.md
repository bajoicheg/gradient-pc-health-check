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
12. Если GUI работает standard user и требуется UAC, actual remediation worker запускается только из `%ProgramFiles%`. Если исходный EXE открыт из Downloads/другого user-writable location, используется отдельный elevated bootstrap, который сначала создаёт и SHA-256-проверяет копию в Program Files.

## UAC, bootstrap и IPC

Для результата elevated worker **не пишет privileged-файл в пользовательский Temp**. Это исключает класс атак с подменой каталога/junction для privileged file write.

Перед запуском worker GUI:

- генерирует случайный GUID session id;
- создаёт локальный named pipe `GradientPcHealthCheck-<GUID>`;
- генерирует 256-битный случайный nonce;
- передаёт только session id, allow-listed action IDs, ограниченный Temp age, pipe name и nonce;
- если текущий EXE уже расположен под Program Files — запускает его через `runas` сразу в worker-mode;
- если текущий EXE находится в user-writable location — запускает через `runas` bootstrap-mode.

Bootstrap:

- требует административный токен;
- создаёт `%ProgramFiles%\Gradient\PCHealthCheck`;
- копирует текущий EXE во временное staging-имя в этом каталоге;
- сверяет SHA-256 исходного и staging-файла;
- атомарно/с заменой публикует `Gradient-PC-Health-Check.exe`;
- повторно сверяет SHA-256;
- запускает уже Program Files-копию как actual worker с унаследованным elevated token;
- не выполняет remediation самостоятельно.

Worker:

- проверяет формат session/pipe/nonce;
- повторно фильтрует список действий по hardcoded allow-list;
- требует административный токен для `CleanTemp`, `Dism`, `Sfc`;
- подключается к named pipe **до** запуска долгих действий, чтобы GUI быстро мог подтвердить успешный bootstrap/elevation;
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
- автоматическое применение DISM/SFC без явной галочки инженера.

## Развёртывание

Рекомендуемый постоянный путь:

`%ProgramFiles%\Gradient\PCHealthCheck\Gradient-PC-Health-Check.exe`

Централизованное развёртывание (Kaspersky/Intune/SCCM/другой корпоративный software deployment) остаётся предпочтительным вариантом: при нём bootstrap не нужен, а происхождение бинарника можно контролировать заранее.

Для пилота допускается запуск проверенного EXE из Downloads. При первом выбранном административном действии 0.3.1 автоматически создаёт SHA-256-проверенную рабочую копию в Program Files и выполняет remediation уже из неё.

## Подпись

CI формирует SHA-256. Authenticode в текущей версии не выполняется, так как сертификат подписи в репозитории отсутствует. Перед широким корпоративным развёртыванием рекомендуется подписывать release EXE корпоративным code-signing сертификатом и проверять publisher средствами AppLocker/WDAC/EDR.
