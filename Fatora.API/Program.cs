

using Fatora.API;
using Fatora.API.Exceptions;
using Fatora.API.Filters;
using Fatora.BL.Utils;
using Fatora.DAL.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddGroupedServices();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:DefaultConnection is not configured. Set it in appsettings.Development.json locally, " +
        "or the ConnectionStrings__DefaultConnection environment variable in production.");
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;

    var JwtSettings = builder.Configuration.GetSection("JwtSettings");
    var secretKey = JwtSettings["SecretKey"];
    if (string.IsNullOrEmpty(secretKey))
    {
        throw new InvalidOperationException(
            "JwtSettings:SecretKey is not configured. Set it in appsettings.Development.json locally, " +
            "or the JwtSettings__SecretKey environment variable in production - never in committed appsettings.json.");
    }

    options.TokenValidationParameters = new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        ValidateIssuerSigningKey = true,
        ValidIssuer = JwtSettings["Issuer"],
        ValidAudience = JwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        RoleClaimType = "role"

    };
});

builder.Services.AddAuthorization();

// The only endpoints an unauthenticated caller can reach are the credential
// ones, and each of them answers a guess: a password, a QR token, or a 6-digit
// recovery code. Without a limiter, guessing is bounded only by how fast the
// host will answer. Partitioned by client IP, which UseForwardedHeaders below
// resolves to the real caller rather than the platform's edge proxy.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Sign-in. Deliberately generous, because one human sign-in is not one
    // request here: ApiClient silently re-sends a timed-out request, and its
    // 30s timeout sits barely above Render's ~24s cold start, so the first
    // login of the day can bill twice; a SubAdmin QR scan costs two more,
    // since the login page tries /reps/login-by-qr before
    // /subadmins/login-by-qr. Worse, this partitions on public IP, and reps
    // work on mobile data behind carrier NAT - so the bucket is shared by
    // users with no relationship to each other. A tight limit here would
    // lock a shop out of its own app at shift start, which is far more
    // damaging than the attack it prevents. 200 per 5 minutes still caps a
    // sweep at 57,600/day, nowhere near a password or QR-token keyspace.
    options.AddPolicy(RateLimitPolicies.SignIn, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(5)
            }));

    // Admin password recovery. A human does this once, rarely - so this can be
    // strict, and none of the amplifiers above apply to it. Together with
    // AdminRecoveryService's single-outstanding-code rule and its 5-attempt
    // cap, it puts the 9x10^5 code keyspace out of reach.
    options.AddPolicy(RateLimitPolicies.PasswordRecovery, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15)
            }));
});

builder.Services.AddControllers(options =>
{
    options.Filters.Add<AccountStatusFilter>();
}).AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var dbContext = services.GetRequiredService<AppDbContext>();

    // Apply pending migrations
    await dbContext.Database.MigrateAsync();

    // Seed roles and admin user
    await SeedData.seedAuthDataAysnc(services);
}



// Configure the HTTP request pipeline.

// Render (and most PaaS hosts) terminate HTTPS at their edge and forward plain HTTP to the
// container. Without this, the app sees every request as "http" and UseHttpsRedirection below
// would issue a redirect loop. KnownNetworks/KnownProxies are cleared because the host's edge
// proxy isn't on a known local subnet - the container has no other inbound path anyway.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// Both lines are order-critical. UseForwardedHeaders must come first, because
// the limiter partitions on the client IP and that is only the real caller once
// the forwarded headers have been applied - without it every request behind the
// platform's edge proxy would share a single bucket. UseRouting must come
// before the limiter for the [EnableRateLimiting] endpoint metadata to be
// resolved at all; it is stated explicitly rather than relying on the one
// WebApplication auto-inserts, because the day anyone adds a UseRouting of
// their own further down the pipeline, all five policies would silently stop
// applying - no error, no failing test.
app.UseRouting();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    // Mobile clients (emulator/device) talk to the local dev API over plain
    // HTTP; the dev HTTPS certificate isn't trusted on-device, so redirecting
    // to it would break every request. Only enforce HTTPS outside Development.
    app.UseHttpsRedirection();
}


app.UseStaticFiles();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () =>
{
    return Results.Ok(new {Message="all up"});
});
app.Run();

