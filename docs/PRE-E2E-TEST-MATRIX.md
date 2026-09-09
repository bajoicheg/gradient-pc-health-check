# Pre-E2E test matrix

| Сценарий | Ожидаемый результат |
|---|---|
| Полная healthy-телеметрия | высокий coverage, статус OK, score без штрафов |
| CPU/RAM/disk telemetry отсутствует | coverage снижен; приложение не должно трактовать пропуск как «норма» |
| Нет физических дисков/health | coverage снижен, но нет ложного CRIT |
| Один краткий performance spike | медианное sampling не должно само по себе давать ложный CRIT |
| Много Windows Error, но они распределены по разным Provider/Event ID | не должно быть сильного штрафа только за raw count |
| Повторяющаяся ошибка одного Provider/Event ID | должна подниматься как диагностически значимый сигнал |
| Pending reboot | WARN + ручная рекомендация перезагрузки |
| Мало места на C: | WARN/CRIT + preselected CleanTemp при достижении порога |
| DISM + SFC выбраны вместе | DISM выполняется первым |
| UAC отменён | remediation не выполняется; GUI получает понятное сообщение |
| EXE запущен из Downloads | bootstrap копирует SHA-256-проверенную копию в Program Files |
| Worker получает неизвестный action id | действие отбрасывается allow-list |
