using Microsoft.EntityFrameworkCore;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Infrastructure.Context;
using SyncApp26.Infrastructure.Repositories;
using SyncApp26.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using System.IO;
using SyncApp26.API.Services;
using SyncApp26.API.Filters;
using SyncApp26.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSignalR();
builder.Services.AddControllers(options =>
    {
        // Global, fail-closed: any non-GET request on an impersonation token is refused unless the
        // action is explicitly marked [AllowDuringImpersonation]. See ImpersonationReadOnlyFilter.
        options.Filters.Add<ImpersonationReadOnlyFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.MaxDepth = 64;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure CORS for Angular frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200", "http://localhost:5022")  // Angular dev server and API
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Configure EF Core context and resolve relative SQLite path against ContentRoot.
var configuredConnection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
var sqliteBuilder = new SqliteConnectionStringBuilder(configuredConnection);
if (!Path.IsPathRooted(sqliteBuilder.DataSource))
{
    var basePath = builder.Environment.ContentRootPath;
    sqliteBuilder.DataSource = Path.GetFullPath(Path.Combine(basePath, sqliteBuilder.DataSource));
}
sqliteBuilder.Mode = SqliteOpenMode.ReadWriteCreate;
sqliteBuilder.Cache = SqliteCacheMode.Shared;
sqliteBuilder.Pooling = true;
sqliteBuilder.DefaultTimeout = 60;
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(sqliteBuilder.ToString(), sqliteOptions => sqliteOptions.CommandTimeout(60)));

// Repositories
builder.Services.AddScoped<IDepartmentRepository, DepartmentRepository>();
builder.Services.AddScoped<IWorkSiteRepository, WorkSiteRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserChangeHistoryRepository, UserChangeHistoryRepository>();
builder.Services.AddScoped<IImportHistoryRepository, ImportHistoryRepository>();
builder.Services.AddScoped<IFunctionRepository, FunctionRepository>();
builder.Services.AddScoped<IDepartmentFunctionRepository, DepartmentFunctionRepository>();
builder.Services.AddScoped<IUserSignatureRepository, UserSignatureRepository>();
builder.Services.AddScoped<IDataChangeRequestRepository, DataChangeRequestRepository>();
builder.Services.AddScoped<IUserInitialTrainingRepository, UserInitialTrainingRepository>();
builder.Services.AddScoped<IImpersonationLogRepository, ImpersonationLogRepository>();
builder.Services.AddScoped<ISignatureAnomalyAlertRepository, SignatureAnomalyAlertRepository>();


// Services
builder.Services.AddScoped<IDepartmentService, DepartmentService>();
builder.Services.AddScoped<IWorkSiteService, WorkSiteService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ICsvSyncService, CsvSyncService>();
builder.Services.AddScoped<ICsvValidationService, CsvValidationService>();
builder.Services.AddScoped<ISyncNotificationService, SyncNotificationService>();
builder.Services.AddScoped<IImportHistoryService, ImportHistoryService>();
builder.Services.AddScoped<IUserChangeHistoryService, UserChangeHistoryService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IDocumentSignatureService, DocumentSignatureService>();
builder.Services.AddScoped<IFunctionService, FunctionService>();
builder.Services.AddScoped<IDepartmentFunctionService, DepartmentFunctionService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<ISignatureVerificationService, SignatureVerificationService>();
builder.Services.AddScoped<IPeriodicTrainingService, PeriodicTrainingService>();
builder.Services.AddScoped<IUserSignatureService, UserSignatureService>();
builder.Services.AddScoped<IDataChangeRequestService, DataChangeRequestService>();
builder.Services.AddScoped<IUserInitialTrainingService, UserInitialTrainingService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IImpersonationService, ImpersonationService>();
builder.Services.AddScoped<IDocumentSigningService, DocumentSigningService>();
builder.Services.AddSingleton<ICryptographyService, CryptographyService>();
builder.Services.AddSingleton<ISignatureKeyProvider, ConfigSignatureKeyProvider>();
builder.Services.AddSingleton<IHmacSignatureService, HmacSignatureService>();
builder.Services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();
builder.Services.AddSingleton<IMicrosoftTokenValidator, MicrosoftTokenValidator>();

// Background Services
builder.Services.AddHostedService<DepartmentCleanupService>();
builder.Services.AddScoped<SignatureVerificationSweeper>();
builder.Services.AddHostedService<SignatureVerificationSweepService>();

// Since .NET 6, an unhandled exception from a hosted service's ExecuteAsync stops the entire host
// by default. A background job (e.g. an SMTP failure while emailing an anomaly alert) must never be
// able to take down the whole API — each service already catches what it knows about internally,
// this is the outer safety net for anything that slips through.
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

// JWT Authentication
var jwtSecretKey = builder.Configuration["JwtSettings:SecretKey"]
    ?? throw new InvalidOperationException("JwtSettings:SecretKey is not configured.");
var key = Encoding.ASCII.GetBytes(jwtSecretKey);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.Zero
    };
    // SignalR's browser transport can't set an Authorization header, so the client sends the
    // token as ?access_token= instead — only honored for the hub path.
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

// Seed the database
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();

        // Only seed a genuinely empty database - avoids re-inserting default data on every run.
        if (!await context.Departments.AnyAsync() && !await context.Users.AnyAsync())
        {
            await DatabaseSeeder.SeedAsync(context);
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<SyncApp26.API.Hubs.SyncHub>("/hubs/sync");

app.Run();
