using SyncApp26.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace SyncApp26.API.Services
{
    public class DepartmentCleanupService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DepartmentCleanupService> _logger;

        public DepartmentCleanupService(IServiceProvider serviceProvider, ILogger<DepartmentCleanupService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Department Cleanup Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CleanupOldDepartmentsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing Department Cleanup.");
                }

                // Run once a day
                await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
            }
        }

        private async Task CleanupOldDepartmentsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var thresholdDate = DateTime.UtcNow.AddDays(-90);

            // Find departments deactivated > 90 days ago that have NO users assigned
            var departmentsToDelete = await context.Departments
                .Where(d => d.DeletedAt != null && 
                            d.DeletedAt <= thresholdDate && 
                            !d.Users.Any())
                .ToListAsync(stoppingToken);

            if (departmentsToDelete.Any())
            {
                _logger.LogInformation($"Found {departmentsToDelete.Count} old, empty departments to permanently delete.");
                context.Departments.RemoveRange(departmentsToDelete);
                await context.SaveChangesAsync(stoppingToken);
                _logger.LogInformation("Permanent deletion complete.");
            }
        }
    }
}
