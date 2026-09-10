using System.Globalization;

namespace SyncApp26.API.Services.Logging
{
    public enum LogRetentionSchedule
    {
        Startup,
        Daily,

        Interval
    }

    public static class LogRetentionScheduler
    {
        public const LogRetentionSchedule DefaultSchedule = LogRetentionSchedule.Daily;
        public static readonly TimeOnly DefaultDailyAtLocalTime = new(3, 15);

        public static LogRetentionSchedule ParseSchedule(string? configuredValue) =>
            Enum.TryParse<LogRetentionSchedule>(configuredValue, ignoreCase: true, out var parsed)
                ? parsed
                : DefaultSchedule;

        public static TimeOnly ParseDailyAtLocalTime(string? configuredValue) =>
            TimeOnly.TryParse(configuredValue, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : DefaultDailyAtLocalTime;

        public static TimeSpan GetDelayUntilNextDailyRun(DateTimeOffset nowLocal, TimeOnly targetLocalTime)
        {
            var todayAtTarget = new DateTimeOffset(nowLocal.Date, nowLocal.Offset) + targetLocalTime.ToTimeSpan();
            var next = todayAtTarget > nowLocal ? todayAtTarget : todayAtTarget.AddDays(1);
            return next - nowLocal;
        }
    }
}
