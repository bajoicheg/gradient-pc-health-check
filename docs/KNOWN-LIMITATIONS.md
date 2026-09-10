# Known limitations before E2E

1. До проверки на реальном Windows 11 нельзя считать подтверждёнными UAC под отдельной доменной admin-учёткой, named-pipe IPC и работу канонической Program Files copy во всех корпоративных GPO/EDR-сценариях.
2. WMI/CIM и SecurityCenter2 могут вести себя по-разному на корпоративных образах; отсутствие данных не должно интерпретироваться как исправность.
3. Event Log пока агрегирует Critical/Error по System/Application; после первых E2E нужна калибровка того, какие повторяющиеся Provider/Event ID действительно коррелируют с жалобой «ПК тормозит».
4. DISM/SFC остаются ручным выбором инженера и не должны становиться автоматической «оптимизацией».
5. Authenticode code signing пока не включён; текущая сборка контролируется SHA-256. По этой причине административные remediation из Downloads/user-writable path в 0.3.4 заблокированы.
6. Пороговые значения Health Score являются эвристикой Service Desk и требуют калибровки на реальных инцидентах.
7. Trusted remediation path пока один: `%ProgramFiles%\Gradient\PCHealthCheck\Gradient-PC-Health-Check.exe`. До централизованного deployment административные действия не считаются готовыми к широкому использованию.
