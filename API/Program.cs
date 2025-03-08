using API.Middleware;
using API.SignalR;
using Application.Activities.Queries;
using Application.Activities.Validators;
using Application.Core;
using Application.Interfaces;
using Domain;
using FluentValidation;
using Infrastructure.Photos;
using Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Persistence;

var builder = WebApplication.CreateBuilder(args);


var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(15),
            errorCodesToAdd: null);
        npgsqlOptions.CommandTimeout(120);
    }));
// Add services to the container.
builder.Services.AddControllers(opt => 
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    opt.Filters.Add(new AuthorizeFilter(policy));
});

// Fixed DbContext configuration to use single context for both app and identity
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    string connStr;

    if (env == "Development")
    {
          var devConnStr = builder.Configuration.GetConnectionString("DefaultConnection");
    if (devConnStr == null)
    {
        throw new InvalidOperationException(
            "Connection string 'DefaultConnection' not found in Development configuration");
    }
    connStr = devConnStr;
    }
    else
    {
        var connUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        connUrl = connUrl?.Replace("postgres://", string.Empty);
        
        if (connUrl == null)
            throw new ArgumentNullException("DATABASE_URL environment variable not set");

        var pgUserPass = connUrl.Split("@")[0];
        var pgHostPortDb = connUrl.Split("@")[1];
        var pgHostPort = pgHostPortDb.Split("/")[0];
        var pgDb = pgHostPortDb.Split("/")[1];
        var pgUser = pgUserPass.Split(":")[0];
        var pgPass = pgUserPass.Split(":")[1];
        var pgHost = pgHostPort.Split(":")[0];
        var pgPort = pgHostPort.Split(":")[1];
        var updatedHost = pgHost.Replace("flycast", "internal");

        connStr = $"Server={updatedHost};Port={pgPort};User Id={pgUser};Password={pgPass};Database={pgDb};";
    }

    options.UseNpgsql(connStr);
});

builder.Services.AddCors();
builder.Services.AddSignalR();
builder.Services.AddMediatR(cfg => 
{
    cfg.RegisterServicesFromAssemblyContaining<GetActivityList.Handler>();
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

builder.Services.AddScoped<IUserAccessor, UserAccessor>();
builder.Services.AddScoped<IPhotoService, PhotoService>();
builder.Services.AddAutoMapper(typeof(MappingProfiles).Assembly);
builder.Services.AddValidatorsFromAssemblyContaining<CreateActivityValidator>();
builder.Services.AddTransient<ExceptionMiddleware>();

// Fixed Identity configuration to use AppDbContext
builder.Services.AddIdentityApiEndpoints<User>(options => 
{
    options.User.RequireUniqueEmail = true;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("IsActivityHost", policy => 
    {
        policy.Requirements.Add(new IsHostRequirement());
    });
});

builder.Services.AddTransient<IAuthorizationHandler, IsHostRequirementHandler>();
builder.Services.Configure<CloudinarySettings>(builder.Configuration.GetSection("CloudinarySettings"));

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();

// Updated CORS configuration with proper policy
app.UseCors(policy => 
{
    policy
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()
        .WithOrigins(builder.Configuration["ClientUrl"] ?? "http://localhost:3000");
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGroup("api").MapIdentityApi<User>();
app.MapHub<CommentHub>("/comments");

// Database migration and seeding
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<AppDbContext>();
        var userManager = services.GetRequiredService<UserManager<User>>();
        await context.Database.MigrateAsync();
        await DbInitializer.SeedData(context, userManager);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred during migration or seeding");
    }
}

app.Run();