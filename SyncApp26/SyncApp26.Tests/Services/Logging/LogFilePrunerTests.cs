using SyncApp26.API.Services.Logging;

namespace SyncApp26.Tests.Services.Logging
{
    public class LogFilePrunerTests
    {
        // ───────────────────────── TryParse ─────────────────────────

        [Fact]
        public void TryParse_FirstFileOfTheDay_HasSequenceZero()
        {
            var parsed = LogFilePruner.TryParse("syncapp-20260825.log", out var date, out var seq);

            Assert.True(parsed);
            Assert.Equal(new DateOnly(2026, 8, 25), date);
            Assert.Equal(0, seq);
        }

        [Fact]
        public void TryParse_SizeTriggeredRoll_ReadsTheSequenceNumber()
        {
            var parsed = LogFilePruner.TryParse("syncapp-20260825_003.log", out var date, out var seq);

            Assert.True(parsed);
            Assert.Equal(new DateOnly(2026, 8, 25), date);
            Assert.Equal(3, seq);
        }

        [Fact]
        public void TryParse_DifferentPrefix_StillParses()
        {
            // The error sink rolls under a different file name prefix than the main sink.
            var parsed = LogFilePruner.TryParse("error-20260825_010.log", out var date, out var seq);

            Assert.True(parsed);
            Assert.Equal(new DateOnly(2026, 8, 25), date);
            Assert.Equal(10, seq);
        }

        [Theory]
        [InlineData("readme.txt")]
        [InlineData("syncapp.log")]
        [InlineData("syncapp-2026-08-25.log")]
        [InlineData("syncapp-20260825.txt")]
        [InlineData("")]
        public void TryParse_UnrecognizedName_ReturnsFalse(string fileName)
        {
            var parsed = LogFilePruner.TryParse(fileName, out _, out _);

            Assert.False(parsed);
        }

        // ───────────────────────── SelectFilesToDelete ─────────────────────────

        private static readonly DateOnly Today = new(2026, 8, 25);

        [Fact]
        public void SelectFilesToDelete_DayWithinBudget_DeletesNothing()
        {
            var files = new[] { "syncapp-20260825.log", "syncapp-20260825_001.log" };

            var toDelete = LogFilePruner.SelectFilesToDelete(files, maxFilesPerDay: 8, retentionDays: 10, Today);

            Assert.Empty(toDelete);
        }

        [Fact]
        public void SelectFilesToDelete_DayOverBudget_DeletesTheOldestRollsFirst()
        {
            // 10 files for today, budget is 8 -- the two oldest (lowest sequence) rolls must go,
            // the eight most recent must survive.
            var files = Enumerable.Range(0, 10)
                .Select(i => i == 0 ? "syncapp-20260825.log" : $"syncapp-20260825_{i:000}.log")
                .ToArray();

            var toDelete = LogFilePruner.SelectFilesToDelete(files, maxFilesPerDay: 8, retentionDays: 10, Today);

            Assert.Equal(2, toDelete.Count);
            Assert.Contains("syncapp-20260825.log", toDelete);
            Assert.Contains("syncapp-20260825_001.log", toDelete);
            Assert.DoesNotContain("syncapp-20260825_009.log", toDelete);
            Assert.DoesNotContain("syncapp-20260825_008.log", toDelete);
        }

        [Fact]
        public void SelectFilesToDelete_FileOlderThanRetentionWindow_IsDeletedRegardlessOfDailyCount()
        {
            var elevenDaysAgo = Today.AddDays(-11);
            var files = new[] { $"syncapp-{elevenDaysAgo:yyyyMMdd}.log" };

            var toDelete = LogFilePruner.SelectFilesToDelete(files, maxFilesPerDay: 8, retentionDays: 10, Today);

            Assert.Single(toDelete);
        }

        [Fact]
        public void SelectFilesToDelete_FileExactlyAtRetentionBoundary_IsDeleted()
        {
            // "Removed after 10 days" -- a file that is exactly 10 days old is the first one gone.
            var tenDaysAgo = Today.AddDays(-10);
            var files = new[] { $"syncapp-{tenDaysAgo:yyyyMMdd}.log" };

            var toDelete = LogFilePruner.SelectFilesToDelete(files, maxFilesPerDay: 8, retentionDays: 10, Today);

            Assert.Single(toDelete);
        }

        [Fact]
        public void SelectFilesToDelete_FileJustInsideRetentionWindow_Survives()
        {
            var nineDaysAgo = Today.AddDays(-9);
            var files = new[] { $"syncapp-{nineDaysAgo:yyyyMMdd}.log" };

            var toDelete = LogFilePruner.SelectFilesToDelete(files, maxFilesPerDay: 8, retentionDays: 10, Today);

            Assert.Empty(toDelete);
        }

        [Fact]
        public void SelectFilesToDelete_UnrecognizedFileName_IsNeverSelected()
        {
            var files = new[] { "readme.txt", "syncapp-20260825.log" };

            var toDelete = LogFilePruner.SelectFilesToDelete(files, maxFilesPerDay: 0, retentionDays: 10, Today);

            Assert.DoesNotContain("readme.txt", toDelete);
        }

        [Fact]
        public void SelectFilesToDelete_MultipleDays_AreBudgetedIndependently()
        {
            var yesterday = Today.AddDays(-1);
            var files = new[]
            {
                // Today: 3 files, budget 2 -- one must go.
                "syncapp-20260825.log", "syncapp-20260825_001.log", "syncapp-20260825_002.log",
                // Yesterday: 1 file, well within budget -- must survive.
                $"syncapp-{yesterday:yyyyMMdd}.log"
            };

            var toDelete = LogFilePruner.SelectFilesToDelete(files, maxFilesPerDay: 2, retentionDays: 10, Today);

            Assert.Single(toDelete);
            Assert.Contains("syncapp-20260825.log", toDelete);
            Assert.DoesNotContain($"syncapp-{yesterday:yyyyMMdd}.log", toDelete);
        }
    }
}
