namespace Gradient.PcHealthCheck;

public sealed class AssessmentService
{
    public Thresholds Thresholds { get; } = new();

    public ScanResult Assess(DiagnosticData data)
    {
        var findings = new List<Finding>();
        void Add(string sev, string cat, string title, string value, string rec, int penalty)
            => findings.Add(new Finding { Severity = sev, Category = cat, Title = title, Value = value, Recommendation = rec, Penalty = penalty });

        var p = data.Performance;
        if (p.CpuPercent is double cpu)
        {
            if (cpu >= Thresholds.CpuCriticalPercent) Add("CRIT", "CPU", "Критическая загрузка CPU", $"{cpu:0.#}%", "Проверьте TOP процессов по CPU и фоновые задачи.", 15);
            else if (cpu >= Thresholds.CpuWarnPercent) Add("WARN", "CPU", "Высокая загрузка CPU", $"{cpu:0.#}%", "Проверьте TOP процессов по CPU.", 7);
            else Add("OK", "CPU", "Загрузка CPU", $"{cpu:0.#}%", "Текущая загрузка в обычном диапазоне.", 0);
        }

        if (p.MemoryAvailablePercent is double mem)
        {
            if (mem <= Thresholds.MemoryCriticalAvailablePercent) Add("CRIT", "RAM", "Критически мало свободной памяти", $"{mem:0.#}% доступно", "Проверьте TOP процессов по RAM и объём установленной памяти.", 15);
            else if (mem <= Thresholds.MemoryWarnAvailablePercent) Add("WARN", "RAM", "Мало свободной памяти", $"{mem:0.#}% доступно", "Проверьте TOP процессов по RAM.", 7);
            else Add("OK", "RAM", "Свободная память", $"{mem:0.#}% доступно", "Запас оперативной памяти нормальный.", 0);
        }

        var systemDrive = data.LogicalDisks.FirstOrDefault(x => string.Equals(x.Drive, Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                          ?? data.LogicalDisks.FirstOrDefault(x => string.Equals(x.Drive, "C:", StringComparison.OrdinalIgnoreCase));
        if (systemDrive is not null)
        {
            if (systemDrive.FreePercent <= Thresholds.SystemDiskCriticalFreePercent || systemDrive.FreeGB <= Thresholds.SystemDiskCriticalFreeGB)
                Add("CRIT", "Диск", "Критически мало места на системном диске", $"{systemDrive.FreeGB:0.#} GB ({systemDrive.FreePercent:0.#}%)", "Освободите место. Temp можно очистить безопасной remediation.", 15);
            else if (systemDrive.FreePercent <= Thresholds.SystemDiskWarnFreePercent || systemDrive.FreeGB <= Thresholds.SystemDiskWarnFreeGB)
                Add("WARN", "Диск", "Мало места на системном диске", $"{systemDrive.FreeGB:0.#} GB ({systemDrive.FreePercent:0.#}%)", "Проверьте крупные данные и временные файлы.", 7);
            else Add("OK", "Диск", "Свободное место на системном диске", $"{systemDrive.FreeGB:0.#} GB ({systemDrive.FreePercent:0.#}%)", "Запас свободного места нормальный.", 0);
        }

        if (p.DiskQueueLength is double queue)
        {
            if (queue >= Thresholds.DiskQueueCritical) Add("CRIT", "Диск", "Высокая очередь диска", $"{queue:0.#}", "Проверьте TOP процессов по I/O и состояние накопителя.", 12);
            else if (queue >= Thresholds.DiskQueueWarn) Add("WARN", "Диск", "Повышенная очередь диска", $"{queue:0.#}", "Проверьте TOP процессов по I/O.", 5);
        }

        var unhealthy = data.PhysicalDisks.Where(x => !IsHealthyDisk(x.HealthStatus)).ToList();
        if (unhealthy.Count > 0)
            Add("CRIT", "Накопитель", "Состояние физического диска требует внимания", string.Join("; ", unhealthy.Select(x => $"{x.Name}: {x.HealthStatus}")), "Проверьте SMART/диагностику производителя и резервное копирование.", 20);

        if (data.System.UptimeDays >= Thresholds.UptimeCriticalDays)
            Add("WARN", "Windows", "Очень большой uptime", $"{data.System.UptimeDays:0.#} дней", "Согласуйте перезагрузку после проверки активных задач.", 5);
        else if (data.System.UptimeDays >= Thresholds.UptimeWarnDays)
            Add("WARN", "Windows", "Большой uptime", $"{data.System.UptimeDays:0.#} дней", "Рассмотрите плановую перезагрузку.", 3);
        else Add("OK", "Windows", "Uptime", $"{data.System.UptimeDays:0.#} дней", "Uptime в обычном диапазоне.", 0);

        if (data.PendingReboot.Pending)
            Add("WARN", "Windows", "Ожидается перезагрузка", string.Join("; ", data.PendingReboot.Reasons), "Согласуйте перезагрузку после сохранения данных пользователя.", 5);

        var errors = data.Events.CriticalCount + data.Events.ErrorCount;
        if (errors >= Thresholds.RecentErrorCriticalCount) Add("CRIT", "События", "Много критических/ошибочных событий", $"{errors} за {data.Events.Hours} ч", "Изучите повторяющиеся Provider/Event ID.", 10);
        else if (errors >= Thresholds.RecentErrorWarnCount) Add("WARN", "События", "Повышенное число ошибок Windows", $"{errors} за {data.Events.Hours} ч", "Проверьте повторяющиеся Event ID и Provider.", 4);

        if (data.StartupItems.Count >= Thresholds.StartupWarnCount)
            Add("WARN", "Автозагрузка", "Много элементов автозагрузки", data.StartupItems.Count.ToString(), "Не отключайте автоматически: проверьте назначение и владельца ПО.", 3);

        var penalty = findings.Sum(x => x.Penalty);
        var score = Math.Clamp(100 - penalty, 0, 100);
        var status = score >= 85 ? "OK" : score >= 65 ? "WARN" : "CRIT";

        return new ScanResult
        {
            Data = data,
            Assessment = new Assessment { Score = score, Status = status, Findings = findings },
            Actions = BuildActions(data)
        };
    }

    private List<ActionRecommendation> BuildActions(DiagnosticData data)
    {
        var actions = new List<ActionRecommendation>();
        void Add(ActionRecommendation a)
        {
            if (!actions.Any(x => x.Id == a.Id)) actions.Add(a);
        }

        var systemDrive = data.LogicalDisks.FirstOrDefault(x => string.Equals(x.Drive, "C:", StringComparison.OrdinalIgnoreCase));
        if (systemDrive is not null && (systemDrive.FreePercent <= Thresholds.SystemDiskWarnFreePercent || systemDrive.FreeGB <= Thresholds.SystemDiskWarnFreeGB))
        {
            Add(new ActionRecommendation
            {
                Id = "CleanTemp", Kind = "Рекомендуется", Title = "Очистить старые временные файлы",
                Reason = $"На системном диске свободно {systemDrive.FreeGB:0.#} GB ({systemDrive.FreePercent:0.#}%). Удаляются только Temp-файлы старше {Thresholds.TempOlderThanDays} дней; Prefetch не затрагивается.",
                CanAutomate = true, RequiresAdmin = true, Preselected = true, Risk = "Низкий",
                Verification = "Повторно измерить свободное место и зафиксировать освобождённый объём."
            });
        }

        if (data.PendingReboot.Pending || data.System.UptimeDays >= Thresholds.UptimeWarnDays)
        {
            Add(new ActionRecommendation
            {
                Id = "ManualRestart", Kind = "Вручную", Title = "Согласовать перезагрузку ПК",
                Reason = data.PendingReboot.Pending ? "Windows ожидает перезагрузку: " + string.Join("; ", data.PendingReboot.Reasons) : $"Uptime {data.System.UptimeDays:0.#} дней.",
                CanAutomate = false, RequiresAdmin = false, Risk = "Средний", Verification = "После перезагрузки повторить диагностику."
            });
        }

        if (data.Performance.CpuPercent >= Thresholds.CpuWarnPercent)
            Add(new ActionRecommendation { Id = "InspectCpu", Kind = "Вручную", Title = "Разобрать TOP процессов по CPU", Reason = "Текущая загрузка CPU выше порога.", CanAutomate = false, Risk = "Низкий", Verification = "Определить процесс/задачу и подтвердить устойчивую нагрузку несколькими замерами." });
        if (data.Performance.MemoryAvailablePercent <= Thresholds.MemoryWarnAvailablePercent)
            Add(new ActionRecommendation { Id = "InspectMemory", Kind = "Вручную", Title = "Разобрать TOP процессов по RAM", Reason = "Доступной памяти меньше рабочего порога.", CanAutomate = false, Risk = "Низкий", Verification = "Определить источник потребления памяти; не завершать процесс без понимания назначения." });
        if (data.Performance.DiskQueueLength >= Thresholds.DiskQueueWarn)
            Add(new ActionRecommendation { Id = "InspectIo", Kind = "Вручную", Title = "Разобрать TOP процессов по I/O", Reason = "Повышенная очередь диска.", CanAutomate = false, Risk = "Низкий", Verification = "Подтвердить повторными замерами и проверить состояние накопителя." });
        if (data.PhysicalDisks.Any(x => !IsHealthyDisk(x.HealthStatus)))
            Add(new ActionRecommendation { Id = "InspectSmart", Kind = "Вручную", Title = "Эскалировать проверку накопителя", Reason = "Windows сообщает нештатный HealthStatus физического диска.", CanAutomate = false, Risk = "Высокий", Verification = "SMART/диагностика производителя и наличие актуальной резервной копии." });
        if (data.Events.CriticalCount + data.Events.ErrorCount >= Thresholds.RecentErrorWarnCount)
            Add(new ActionRecommendation { Id = "InspectEvents", Kind = "Вручную", Title = "Проверить повторяющиеся ошибки Windows", Reason = "Количество Critical/Error за 24 часа выше порога.", CanAutomate = false, Risk = "Низкий", Verification = "Связать Event ID/Provider с симптомом пользователя." });
        if (data.StartupItems.Count >= Thresholds.StartupWarnCount)
            Add(new ActionRecommendation { Id = "ReviewStartup", Kind = "Вручную", Title = "Проверить автозагрузку", Reason = $"Найдено {data.StartupItems.Count} элементов автозагрузки.", CanAutomate = false, Risk = "Средний", Verification = "Согласовать отключение только подтверждённых необязательных элементов." });

        Add(new ActionRecommendation
        {
            Id = "FlushDns", Kind = "Дополнительно", Title = "Очистить DNS-кэш",
            Reason = "Использовать только при симптомах разрешения имён/сетевых обращений. Не является общей оптимизацией ПК.",
            CanAutomate = true, RequiresAdmin = false, Preselected = false, Risk = "Низкий", Verification = "Повторить обращение к проблемному имени/ресурсу."
        });
        Add(new ActionRecommendation
        {
            Id = "Dism", Kind = "Дополнительно", Title = "DISM RestoreHealth",
            Reason = "Использовать при признаках повреждения хранилища компонентов Windows или как этап ремонта перед SFC.",
            CanAutomate = true, RequiresAdmin = true, Preselected = false, Risk = "Средний", Verification = "Зафиксировать exit code и повторить диагностику."
        });
        Add(new ActionRecommendation
        {
            Id = "Sfc", Kind = "Дополнительно", Title = "SFC /scannow",
            Reason = "Использовать при подозрении на повреждение системных файлов. Если выбран DISM, SFC выполняется после него.",
            CanAutomate = true, RequiresAdmin = true, Preselected = false, Risk = "Средний", Verification = "Зафиксировать exit code и результат повторной диагностики."
        });

        return actions;
    }

    private static bool IsHealthyDisk(string status)
        => string.IsNullOrWhiteSpace(status) || status.Equals("Healthy", StringComparison.OrdinalIgnoreCase) || status.Equals("OK", StringComparison.OrdinalIgnoreCase) || status.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
}
