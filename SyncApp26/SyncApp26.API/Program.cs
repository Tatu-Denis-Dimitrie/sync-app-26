using Microsoft.EntityFrameworkCore;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Enums;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Infrastructure.Context;
using SyncApp26.Infrastructure.Repositories;
using SyncApp26.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using System.IO;
using SyncApp26.API.Services;
using SyncApp26.API.Services.Logging;
using SyncApp26.API.Filters;
using SyncApp26.API.Middleware;
using SyncApp26.API.Extensions;
using SyncApp26.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;
using Serilog;
using Serilog.Events;
using Microsoft.AspNetCore.Antiforgery;


Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // Add services to the container.
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();
    builder.Services.AddSignalR();
    builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
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
        })
        .AddDataAnnotationsLocalization(options =>
            options.DataAnnotationLocalizerProvider = (_, factory) =>
                factory.Create(LocalizationScopes.Validation, typeof(LocalizationService).Assembly.GetName().Name!));
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

    // Per-IP fixed-window partition shared by all rate-limit policies below.
    static RateLimitPartition<string> IpFixedWindow(HttpContext httpContext, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0
            });

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = (context, cancellationToken) =>
        {
            var rejectionLogger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            rejectionLogger.LogWarning(
                "Rate limit exceeded for {IP} on {Path}.",
                context.HttpContext.Connection.RemoteIpAddress, context.HttpContext.Request.Path);

            context.HttpContext.Response.ContentType = "application/json";
            return new ValueTask(context.HttpContext.Response.WriteAsync(
                "{\"message\":\"Too many requests. Try again later.\"}", cancellationToken));
        };

        // Blanket ceiling, layered under any endpoint-specific policy below.
        options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(
            httpContext => IpFixedWindow(httpContext, 300, TimeSpan.FromMinutes(1)));

        options.AddPolicy("login", httpContext => IpFixedWindow(httpContext, 5, TimeSpan.FromMinutes(1)));
        options.AddPolicy("auth-sensitive", httpContext => IpFixedWindow(httpContext, 5, TimeSpan.FromMinutes(1)));
        options.AddPolicy("signing-token", httpContext => IpFixedWindow(httpContext, 10, TimeSpan.FromMinutes(1)));
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
    builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();


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
    builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
    builder.Services.AddScoped<IDocumentSigningService, DocumentSigningService>();
    builder.Services.AddScoped<ILocalizationService, LocalizationService>();
    builder.Services.AddSingleton<ICryptographyService, CryptographyService>();
    builder.Services.AddSingleton<ISignatureKeyProvider, ConfigSignatureKeyProvider>();
    builder.Services.AddSingleton<IHmacSignatureService, HmacSignatureService>();
    builder.Services.AddSingleton<IGoogleTokenValidator, GoogleTokenValidator>();
    builder.Services.AddSingleton<IMicrosoftTokenValidator, MicrosoftTokenValidator>();

    // Background Services
    builder.Services.AddHostedService<DepartmentCleanupService>();
    builder.Services.AddScoped<SignatureVerificationSweeper>();
    builder.Services.AddHostedService<SignatureVerificationSweepService>();
    builder.Services.AddHostedService<LogFileRetentionService>();

    // Since .NET 6, an unhandled exception from a hosted service's ExecuteAsync stops the entire host
    // by default. A background job (e.g. an SMTP failure while emailing an anomaly alert) must never be
    // able to take down the whole API — each service already catches what it knows about internally,
    // this is the outer safety net for anything that slips through.
    builder.Services.Configure<HostOptions>(options =>
    {
        options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
    });

    // Secure is fixed at startup, not derived from Request.IsHttps (unreliable behind a proxy).
    var authCookieOptions = new AuthCookieOptions
    {
        Secure = builder.Configuration.GetValue<bool?>("Auth:Cookie:Secure") ?? !builder.Environment.IsDevelopment()
    };
    builder.Services.AddSingleton(authCookieOptions);

    // Names Angular's HttpXsrfInterceptor already knows, so no client code is needed.
    builder.Services.AddAntiforgery(options =>
    {
        options.HeaderName = "X-XSRF-TOKEN";
        options.Cookie.Name = "syncapp26_antiforgery";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = authCookieOptions.SameSite;
        options.Cookie.SecurePolicy = authCookieOptions.Secure ? CookieSecurePolicy.Always : CookieSecurePolicy.None;
    });

    // JWT Authentication
    var jwtSecretKey = builder.Configuration["JwtSettings:SecretKey"]
        ?? throw new InvalidOperationException("JwtSettings:SecretKey is not configured.");
    var key = Encoding.ASCII.GetBytes(jwtSecretKey);
    var jwtIssuer = builder.Configuration["JwtSettings:Issuer"]
        ?? throw new InvalidOperationException("JwtSettings:Issuer is not configured.");
    var jwtAudience = builder.Configuration["JwtSettings:Audience"]
        ?? throw new InvalidOperationException("JwtSettings:Audience is not configured.");

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
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new JwtBearerEvents
        {
            // Fallback only - never overrides a real Authorization header.
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Request.Headers.Authorization) &&
                    context.Request.Cookies.TryGetValue(authCookieOptions.Name, out var cookieToken))
                {
                    context.Token = cookieToken;
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
        var logger = services.GetRequiredService<ILogger<Program>>();
        try
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            await context.Database.MigrateAsync();

            // Only seed a genuinely empty database - avoids re-inserting default data on every run.
            if (!await context.Departments.AnyAsync() && !await context.Users.AnyAsync())
            {
                await DatabaseSeeder.SeedAsync(context);
                logger.LogInformation("Database seeded with default data.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
        }
    }

    // Configure the HTTP request pipeline.

    app.UseExceptionHandler();

    // Registered first - some downstream middleware short-circuits without calling next(), which
    // would otherwise skip a header middleware placed later.
    app.Use(async (context, next) =>
    {
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Append("X-Frame-Options", "DENY");
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
        if (!context.Request.Path.StartsWithSegments("/swagger"))
        {
            context.Response.Headers.Append("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
        }
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            // Shared caches may not store responses to Authorization-header requests, but no such
            // rule exists for cookies - since auth moved to cookies, this has to be explicit.
            context.Response.Headers.CacheControl = "no-store";
        }
        await next();
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    else
    {
        // Breaks local HTTP testing, so only enabled outside dev.
        app.UseHsts();
    }

    app.UseSerilogRequestLogging(options =>
    {
        const int slowRequestMs = 3000;

        options.MessageTemplate =
            "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

        options.GetLevel = (httpContext, elapsed, exception) =>
        {
            if (exception is not null || httpContext.Response.StatusCode >= 500)
            {
                return LogEventLevel.Error;
            }

            var path = httpContext.Request.Path;
            if (path.StartsWithSegments("/swagger") || path.StartsWithSegments("/hubs"))
            {
                return LogEventLevel.Verbose;
            }

            if (httpContext.Response.StatusCode >= 400)
            {
                return LogEventLevel.Warning;
            }

            return elapsed > slowRequestMs ? LogEventLevel.Warning : LogEventLevel.Debug;
        };
    });

    app.UseHttpsRedirection();
    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    // CSRF check for cookie-authenticated requests. After UseAuthentication so User is resolved.
    app.Use(async (context, next) =>
    {
        var hasAuthorizationHeader = !string.IsNullOrEmpty(context.Request.Headers.Authorization);
        if (!CsrfExemption.IsExempt(context.Request.Method, context.Request.Path.Value, hasAuthorizationHeader))
        {
            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"message\":\"CSRF validation failed.\"}");
                return;
            }
        }

        await next();
    });

    app.MapControllers();
    app.MapHub<SyncApp26.API.Hubs.SyncHub>("/hubs/sync");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "SyncApp26 API terminated unexpectedly during startup.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
