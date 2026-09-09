# Gradient PC Health Check

Внутреннее Windows-приложение Service Desk для первичной диагностики жалоб **«компьютер сильно тормозит»** и безопасного применения выбранных remediation.

## Формат продукта

Пользовательский артефакт — **один `Gradient-PC-Health-Check.exe`**. PowerShell/BAT-скриптов для эксплуатации нет.

Фирменный знак — щит с буквой **G**, визуально родственный G-switcher; линия «пульса» отличает PC Health Check от switcher.

EXE работает в двух внутренних режимах:

- обычный WinForms GUI без обязательных административных прав;
- тот же EXE как короткоживущий UAC worker только для явно выбранных действий, требующих elevation.

## Рабочий сценарий

1. Service Desk запускает EXE.
2. Программа автоматически собирает диагностический снимок.
3. На главном экране видны индекс состояния, CPU, RAM, свободное место, uptime и подробные вкладки.
4. На вкладке **«Рекомендации»** автоматизируемые действия имеют checkbox.
5. Инженер отмечает только нужные действия и нажимает **«Применить выбранное»**.
6. Если нужны административные права и приложение ещё не elevated, Windows показывает стандартный UAC prompt.
7. Elevated worker выполняет только hardcoded allow-list выбранных действий.
8. Результат worker возвращается parent GUI по локальному named pipe, без privileged-записи в пользовательский Temp.
9. После завершения программа автоматически повторяет полную диагностику.
10. Вкладка **«До / после»** показывает результат; формируется HTML + JSON + ZIP для заявки.

## Диагностика

- CPU / RAM / disk queue;
- логические диски и свободное место;
- физические накопители и Health Status;
- TOP процессов по CPU / RAM / I/O;
- uptime и pending reboot;
- Critical/Error события System/Application за 24 часа;
- автозагрузка;
- Windows Update services и последние hotfixes;
- активные сетевые интерфейсы;
- зарегистрированный antivirus + Kaspersky/KES/KEDR services;
- модель ПК, Windows build, CPU, RAM.

Индекс 0–100 — **эвристика triage**, а не доказательство исправности ПК.

## Автоматизируемые действия

| Действие | По умолчанию | Admin | Комментарий |
|---|---:|---:|---|
| CleanTemp | только при нехватке места | Да | удаляются только Temp-файлы старше 3 дней; reparse point не обходятся; Prefetch не затрагивается |
| FlushDns | нет | Нет | только при сетевых/DNS симптомах; не является общей оптимизацией |
| DISM RestoreHealth | нет | Да | инженер выбирает осознанно |
| SFC /scannow | нет | Да | инженер выбирает осознанно; при выборе вместе с DISM выполняется после DISM |

Не автоматизируются: перезагрузка, завершение процессов, отключение служб/автозагрузки, SMART remediation, Winsock reset, очистка Windows Update cache, Prefetch.

## Auto-elevation

Для безопасного автоматического UAC приложение должно быть развёрнуто в:

`%ProgramFiles%\Gradient\PCHealthCheck\Gradient-PC-Health-Check.exe`

Из Downloads/Temp/профиля пользователя диагностика работает, но auto-elevation намеренно не выполняется. Если инженер уже запустил проверенную копию EXE через **Run as administrator**, выбранные административные действия выполняются без повторного UAC.

## Отчёты

`%LOCALAPPDATA%\Gradient\PCHealthCheck\Reports\`

После обычного сканирования создаются HTML и JSON. После remediation создаются verification HTML/JSON и ZIP-пакет для прикрепления к заявке.

## Сборка

GitHub Actions на `windows-latest`:

- restore/build .NET 8 с warnings-as-errors;
- встроенный smoke self-test;
- publish `win-x64` self-contained single-file;
- проверка, что пользовательский publish содержит только один EXE;
- SHA-256;
- artifact `gradient-pc-health-check-windows-x64`.

## Требования

- Windows 11 x64;
- EXE self-contained, отдельная установка .NET не требуется;
- для административных remediation — локальный/доменный администратор через штатный UAC либо уже elevated-процесс.

## Безопасность

См. [`docs/SECURITY.md`](docs/SECURITY.md).

Проект предназначен для Service Desk, а не для распространения пользователям как «Windows optimizer».
