using System.Globalization;
using System.Text.RegularExpressions;

namespace SyncApp26.API.Services.Logging
{
    /// <summary>
    /// Decides which rolling log files to delete, given their file names. Serilog's
    /// rollingInterval: Day plus rollOnFileSizeLimit: true names files "prefix-yyyyMMdd.log" for the
    /// first file of a day and "prefix-yyyyMMdd_NNN.log" for each size-triggered roll after that,
    /// with NNN increasing as the day goes on. That means the sequence number alone orders a day's
    /// files from oldest (absent suffix, i.e. 0) to newest, without ever touching the filesystem for
    /// timestamps -- which is what keeps this class a pure function and unit-testable without disk.
    ///
    /// retainedFileCountLimit / retainedFileTimeLimit on the Serilog sinks themselves are a size-based
    /// backstop, not a per-day cap -- they count files globally, so a single busy day can quietly eat
    /// into an earlier day's retention window. This class enforces the actual "N files per day, gone
    /// after M days" policy on top of that.
    /// </summary>
    public static class LogFilePruner
    {
        private static readonly Regex RollingLogFileName =
            new(@"^.+-(?<date>\d{8})(?:_(?<seq>\d+))?\.log$", RegexOptions.Compiled);

        public static bool TryParse(string fileName, out DateOnly date, out int sequence)
        {
            var match = RollingLogFileName.Match(fileName);
            if (!match.Success ||
                !DateTime.TryParseExact(
                    match.Groups["date"].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                date = default;
                sequence = 0;
                return false;
            }

            date = DateOnly.FromDateTime(parsed);
            sequence = match.Groups["seq"].Success
                ? int.Parse(match.Groups["seq"].Value, CultureInfo.InvariantCulture)
                : 0;
            return true;
        }

        /// <summary>
        /// Given every *.log file name in one directory, returns the subset that should be deleted:
        /// anything at least <paramref name="retentionDays"/> days old, plus the oldest rolls of any
        /// single day that has more than <paramref name="maxFilesPerDay"/> files. File names this
        /// class doesn't recognize as a rolling log are left alone.
        /// </summary>
        public static IReadOnlyList<string> SelectFilesToDelete(
            IEnumerable<string> fileNames, int maxFilesPerDay, int retentionDays, DateOnly today)
        {
            var toDelete = new List<string>();
            var byDay = new Dictionary<DateOnly, List<(string Name, int Seq)>>();

            foreach (var name in fileNames)
            {
                if (!TryParse(name, out var date, out var seq))
                {
                    continue;
                }

                var ageInDays = today.DayNumber - date.DayNumber;
                if (ageInDays >= retentionDays)
                {
                    toDelete.Add(name);
                    continue;
                }

                if (!byDay.TryGetValue(date, out var filesForDay))
                {
                    filesForDay = new List<(string, int)>();
                    byDay[date] = filesForDay;
                }
                filesForDay.Add((name, seq));
            }

            foreach (var filesForDay in byDay.Values)
            {
                var overflow = filesForDay.Count - maxFilesPerDay;
                if (overflow <= 0)
                {
                    continue;
                }

                toDelete.AddRange(filesForDay.OrderBy(f => f.Seq).Take(overflow).Select(f => f.Name));
            }

            return toDelete;
        }
    }
}
