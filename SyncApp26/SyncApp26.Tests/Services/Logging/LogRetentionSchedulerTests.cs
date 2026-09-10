using SyncApp26.API.Services.Logging;

namespace SyncApp26.Tests.Services.Logging
{
    public class LogRetentionSchedulerTests
    {
        // ───────────────────────── ParseSchedule ─────────────────────────

        [Theory]
        [InlineData("Startup", LogRetentionSchedule.Startup)]
        [InlineData("startup", LogRetentionSchedule.Startup)]
        [InlineData("STARTUP", LogRetentionSchedule.Startup)]
        [InlineData("Daily", LogRetentionSchedule.Daily)]
        [InlineData("Interval", LogRetentionSchedule.Interval)]
        public void ParseSchedule_RecognizesEachValueCaseInsensitively(string configured, LogRetentionSchedule expected)
        {
            Assert.Equal(expected, LogRetentionScheduler.ParseSchedule(configured));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Weekly")] // not a real value
        public void ParseSchedule_FallsBackToDailyWhenMissingOrUnrecognized(string? configured)
        {
            Assert.Equal(LogRetentionSchedule.Daily, LogRetentionScheduler.ParseSchedule(configured));
        }

        // ───────────────────────── ParseDailyAtLocalTime ─────────────────────────

        [Fact]
        public void ParseDailyAtLocalTime_ParsesAConfiguredTime()
        {
            Assert.Equal(new TimeOnly(3, 15), LogRetentionScheduler.ParseDailyAtLocalTime("03:15"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not a time")]
        [InlineData("25:99")]
        public void ParseDailyAtLocalTime_FallsBackToTheDefaultWhenMissingOrUnparseable(string? configured)
        {
            Assert.Equal(LogRetentionScheduler.DefaultDailyAtLocalTime, LogRetentionScheduler.ParseDailyAtLocalTime(configured));
        }

        // ───────────────────────── GetDelayUntilNextDailyRun ─────────────────────────

        [Fact]
        public void GetDelayUntilNextDailyRun_BeforeTargetToday_WaitsUntilLaterToday()
        {
            var now = new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.FromHours(3));
            var target = new TimeOnly(3, 15);

            var delay = LogRetentionScheduler.GetDelayUntilNextDailyRun(now, target);

            Assert.Equal(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(15), delay);
        }

        [Fact]
        public void GetDelayUntilNextDailyRun_AfterTargetToday_WaitsUntilTomorrow()
        {
            var now = new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.FromHours(3));
            var target = new TimeOnly(3, 15);

            var delay = LogRetentionScheduler.GetDelayUntilNextDailyRun(now, target);

            // 24h from now back to 03:15 tomorrow: (24:00 - 10:00) + 03:15 = 17:15.
            Assert.Equal(TimeSpan.FromHours(17) + TimeSpan.FromMinutes(15), delay);
        }

        [Fact]
        public void GetDelayUntilNextDailyRun_ExactlyAtTargetNow_SchedulesTomorrowNotZero()
        {
            // A zero-length delay would spin the loop; landing exactly on the target must still
            // produce a full day's wait, not an immediate re-fire.
            var now = new DateTimeOffset(2026, 9, 9, 3, 15, 0, TimeSpan.FromHours(3));
            var target = new TimeOnly(3, 15);

            var delay = LogRetentionScheduler.GetDelayUntilNextDailyRun(now, target);

            Assert.Equal(TimeSpan.FromHours(24), delay);
        }

        [Fact]
        public void GetDelayUntilNextDailyRun_IsAlwaysStrictlyPositive()
        {
            var now = new DateTimeOffset(2026, 9, 9, 3, 14, 59, TimeSpan.FromHours(3));
            var target = new TimeOnly(3, 15);

            var delay = LogRetentionScheduler.GetDelayUntilNextDailyRun(now, target);

            Assert.True(delay > TimeSpan.Zero);
        }

        [Fact]
        public void GetDelayUntilNextDailyRun_PreservesTheOffsetOfNow()
        {
            // A naive implementation that dropped the offset could silently shift the target time
            // by whatever the local UTC offset happens to be.
            var now = new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.FromHours(3));
            var target = new TimeOnly(3, 15);

            var delay = LogRetentionScheduler.GetDelayUntilNextDailyRun(now, target);
            var next = now + delay;

            Assert.Equal(TimeSpan.FromHours(3), next.Offset);
            Assert.Equal(new TimeOnly(3, 15), TimeOnly.FromDateTime(next.DateTime));
        }
    }
}
