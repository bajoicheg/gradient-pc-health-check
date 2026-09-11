namespace G.PcHealthCheck;

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
            else Add("OK", "CPU", "Загрузка CPU", $"{cpu:0.#}%", "Устойчивая загрузка в обычном диапазоне.", 0);
        }

        if (p.MemoryAvailablePercent is double mem)
        {
            if (mem <= Thresholds.MemoryCriticalAvailablePercent) Add("CRIT", "RAM", "Критически мало свободной памяти", $"{mem:0.#}% доступно", "Проверьте TOP процессов по RAM и объём установленной памяти.", 15);
            else if (mem <= Thresholds.MemoryWarnAvailablePercent) Add("WARN", "RAM", "Мало свободной памяти", $"{mem:0.#}% доступно", "Проверьте TOP процессов по RAM.", 7);
            else Add("OK", "RAM", "Свободная память", $"{mem:0.#}% доступно", "Запас оперативной памяти нормальный.", 0);
        }

        var systemDrive = SystemDrive(data);
        if (systemDrive is not null)
        {
            if (systemDrive.FreePercent <= Thresholds.SystemDiskCriticalFreePercent || systemDrive.FreeGB <= Thresholds.SystemDiskCriticalFreeGB)
                Add("CRIT", "Диск", "Критически мало места на системном диске", $"{systemDrive.FreeGB:0.#} GB ({systemDrive.FreePercent:0.#}%)", "Освободите место. Temp текущего пользователя можно очистить безопасной remediation без elevation.", 15);
            else if (systemDrive.FreePercent <= Thresholds.SystemDiskWarnFreePercent || systemDrive.FreeGB <= Thresholds.SystemDiskWarnFreeGB)
                Add("WARN", "Диск", "Мало места на системном диске", $"{systemDrive.FreeGB:0.#} GB ({systemDrive.FreePercent:0.#}%)", "Проверьте крупные данные и временные файлы.", 7);
            else Add("OK", "Диск", "Свободное место на системном диске", $"{systemDrive.FreeGB:0.#} GB ({systemDrive.FreePercent:0.#}%)", "Запас свободного места нормальный.", 0);
        }

        if (p.DiskQueueLength is double queue && p.DiskBusyPercent is double busy)
        {
            if (queue >= Thresholds.DiskQueueCritical && busy >= Thresholds.DiskBusyCriticalPercent)
                Add("CRIT", "Диск", "Подтверждённая перегрузка диска", $"busy {busy:0.#}%; queue {queue:0.#}", "Проверьте TOP процессов по I/O, задержки и состояние накопителя. Высокая очередь учитывается только вместе с высокой занятостью диска.", 12);
            else if (queue >= Thresholds.DiskQueueWarn && busy >= Thresholds.DiskBusyWarnPercent)
                Add("WARN", "Диск", "Повышенная нагрузка на диск", $"busy {busy:0.#}%; queue {queue:0.#}", "Проверьте TOP процессов по I/O и подтвердите устойчивость нагрузки повторным замером.", 5);
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
        var repeated = MaxRepeatedEventCount(data.Events);
        if (errors >= Thresholds.RecentErrorCriticalCount && repeated >= Thresholds.RepeatedEventCriticalCount)
            Add("CRIT", "События", "Много повторяющихся критических/ошибочных событий", $"{errors} за {data.Events.Hours} ч; максимум одного Event ID: {repeated}", "Изучите повторяющиеся Provider/Event ID и свяжите их с симптомом пользователя.", 10);
        else if (errors >= Thresholds.RecentErrorWarnCount && repeated >= Thresholds.RepeatedEventWarnCount)
            Add("WARN", "События", "Повторяющиеся ошибки Windows", $"{errors} за {data.Events.Hours} ч; максимум одного Event ID: {repeated}", "Проверьте повторяющиеся Event ID и Provider. Сам raw count без повторяемости не считается достаточным сигналом.", 4);

        if (data.StartupItems.Count >= Thresholds.StartupWarnCount)
            Add("WARN", "Автозагрузка", "Много элементов автозагрузки", data.StartupItems.Count.ToString(), "Не отключайте автоматически: проверьте назначение и владельца ПО.", 3);

        var coverage = CalculateCoverage(data);
        if (coverage.Percent < Thresholds.CoverageHighPercent || data.CollectionWarnings.Count > 0)
        {
            var missingText = coverage.Missing.Count == 0 ? "ключевые сигналы собраны" : string.Join("; ", coverage.Missing);
            var warningsText = data.CollectionWarnings.Count == 0 ? "" : $" Ошибок/предупреждений сбора: {data.CollectionWarnings.Count}.";
            Add("WARN", "Данные", "Диагностика собрана не полностью", $"coverage {coverage.Percent}% ({coverage.Status})", $"Недоступно: {missingText}.{warningsText} Повторите диагностику или проверьте WMI/CIM/права; отсутствие телеметрии не является признаком исправности.", 0);
        }

        var penalty = findings.Sum(x => x.Penalty);
        var score = Math.Clamp(100 - penalty, 0, 100);
        var hasCritical = findings.Any(x => x.Severity == "CRIT");
        var hasWarning = findings.Any(x => x.Severity == "WARN");
        var status = hasCritical ? "CRIT" : hasWarning || score < 85 ? "WARN" : "OK";

        return new ScanResult
        {
            Data = data,
            Assessment = new Assessment
            {
                Score = score,
                Status = status,
                CoveragePercent = coverage.Percent,
                CoverageStatus = coverage.Status,
                MissingSignals = coverage.Missing,
                Findings = findings
            },
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

        var systemDrive = SystemDrive(data);
        var tempCleanupRecommended = systemDrive is not null &&
            (systemDrive.FreePercent <= Thresholds.SystemDiskWarnFreePercent || systemDrive.FreeGB <= Thresholds.SystemDiskWarnFreeGB);

        Add(new ActionRecommendation
        {
            Id = "CleanTemp",
            Kind = tempCleanupRecommended ? "Рекомендуется" : "Дополнительно",
            Title = "Очистить старые временные файлы пользователя",
            Reason = tempCleanupRecommended
                ? $"На системном диске свободно {systemDrive!.FreeGB:0.#} GB ({systemDrive.FreePercent:0.#}%). Удаляются только обычные файлы из Temp текущего пользователя старше {Thresholds.TempOlderThanDays} дней; reparse points и Prefetch не затрагиваются."
                : $"Свободного места достаточно, поэтому очистка не рекомендуется автоматически. Инженер может запустить её вручную: удаляются только обычные файлы из Temp текущего пользователя старше {Thresholds.TempOlderThanDays} дней; reparse points и Prefetch не затрагиваются.",
            CanAutomate = true,
            RequiresAdmin = false,
            Preselected = tempCleanupRecommended,
            Risk = "Низкий",
            Verification = "Зафиксировать количество удалённых файлов/освобождённый объём и повторно измерить свободное место."
        });

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
            Add(new ActionRecommendation { Id = "InspectCpu", Kind = "Вручную", Title = "Разобрать TOP процессов по CPU", Reason = "Устойчивая загрузка CPU выше порога.", CanAutomate = false, Risk = "Низкий", Verification = "Определить процесс/задачу и подтвердить устойчивую нагрузку несколькими замерами." });
        if (data.Performance.MemoryAvailablePercent <= Thresholds.MemoryWarnAvailablePercent)
            Add(new ActionRecommendation { Id = "InspectMemory", Kind = "Вручную", Title = "Разобрать TOP процессов по RAM", Reason = "Доступной памяти меньше рабочего порога.", CanAutomate = false, Risk = "Низкий", Verification = "Определить источник потребления памяти; не завершать процесс без понимания назначения." });
        if (IsDiskPressureAtLeastWarn(data.Performance))
            Add(new ActionRecommendation { Id = "InspectIo", Kind = "Вручную", Title = "Разобрать TOP процессов по I/O", Reason = "Одновременно повышены медианная занятость диска и очередь I/O.", CanAutomate = false, Risk = "Низкий", Verification = "Подтвердить повторными замерами, проверить задержки и состояние накопителя." });
        if (data.PhysicalDisks.Any(x => !IsHealthyDisk(x.HealthStatus)))
            Add(new ActionRecommendation { Id = "InspectSmart", Kind = "Вручную", Title = "Эскалировать проверку накопителя", Reason = "Windows сообщает нештатный HealthStatus физического диска.", CanAutomate = false, Risk = "Высокий", Verification = "SMART/диагностика производителя и наличие актуальной резервной копии." });
        if (IsEventSignalSignificant(data.Events))
            Add(new ActionRecommendation { Id = "InspectEvents", Kind = "Вручную", Title = "Проверить повторяющиеся ошибки Windows", Reason = "Есть достаточно частая повторяемость одного Provider/Event ID на фоне повышенного общего числа ошибок.", CanAutomate = false, Risk = "Низкий", Verification = "Связать Event ID/Provider с симптомом пользователя." });
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

    private bool IsDiskPressureAtLeastWarn(PerformanceSnapshot p)
        => p.DiskQueueLength is double queue && p.DiskBusyPercent is double busy
           && queue >= Thresholds.DiskQueueWarn && busy >= Thresholds.DiskBusyWarnPercent;

    private (int Percent, string Status, List<string> Missing) CalculateCoverage(DiagnosticData data)
    {
        var points = 0;
        var missing = new List<string>();
        void Signal(bool available, int weight, string name)
        {
            if (available) points += weight;
            else missing.Add(name);
        }

        Signal(data.Performance.CpuPercent.HasValue, 15, "CPU");
        Signal(data.Performance.MemoryAvailablePercent.HasValue, 15, "RAM");
        Signal(SystemDrive(data) is { SizeGB: > 0 }, 20, "системный диск");
        Signal(data.Performance.DiskQueueLength.HasValue && data.Performance.DiskBusyPercent.HasValue, 10, "нагрузка диска (busy + queue)");
        Signal(data.PhysicalDisks.Any(x => !string.IsNullOrWhiteSpace(x.HealthStatus) && !x.HealthStatus.Equals("Unknown", StringComparison.OrdinalIgnoreCase)), 15, "health физического диска");
        Signal(!string.IsNullOrWhiteSpace(data.System.OS) && !string.IsNullOrWhiteSpace(data.System.BuildNumber), 10, "Windows/build");
        Signal(data.TopCpu.Count > 0 || data.TopMemory.Count > 0 || data.TopIo.Count > 0, 10, "TOP процессов");
        Signal(data.SecurityProducts.Count > 0, 5, "антивирус/EDR");

        var status = points >= Thresholds.CoverageHighPercent ? "HIGH" : points >= Thresholds.CoverageWarnPercent ? "MEDIUM" : "LOW";
        return (points, status, missing);
    }

    private bool IsEventSignalSignificant(EventSummary events)
    {
        var errors = events.CriticalCount + events.ErrorCount;
        var repeated = MaxRepeatedEventCount(events);
        return (errors >= Thresholds.RecentErrorCriticalCount && repeated >= Thresholds.RepeatedEventCriticalCount)
               || (errors >= Thresholds.RecentErrorWarnCount && repeated >= Thresholds.RepeatedEventWarnCount);
    }

    private static int MaxRepeatedEventCount(EventSummary events)
        => events.Top.Count == 0 ? 0 : events.Top.Max(x => x.Count);

    private static LogicalDiskInfo? SystemDrive(DiagnosticData data)
    {
        var drive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\');
        return data.LogicalDisks.FirstOrDefault(x => !string.IsNullOrWhiteSpace(drive) && string.Equals(x.Drive, drive, StringComparison.OrdinalIgnoreCase))
               ?? data.LogicalDisks.FirstOrDefault(x => string.Equals(x.Drive, "C:", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHealthyDisk(string status)
        => string.IsNullOrWhiteSpace(status) || status.Equals("Healthy", StringComparison.OrdinalIgnoreCase) || status.Equals("OK", StringComparison.OrdinalIgnoreCase) || status.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
}
