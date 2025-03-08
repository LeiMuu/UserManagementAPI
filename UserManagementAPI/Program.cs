using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using UserManagementAPI.DbContexts;
using UserManagementAPI.DbSeeders;
using UserManagementAPI.Models;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to listen on port 44395 with SSL
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(44395, listenOptions =>
    {
        listenOptions.UseHttps();
    });
});

// Setting up Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/app.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();
builder.Host.UseSerilog();

#region Services

// Registering an In-Memory Database
builder.Services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase("UserDb"));
builder.Services.AddScoped<DbSeeder>();

// Registering an authentication service
// Ensure the secret key is at least 32 bytes (256 bits)
var secretKey = "your_long_super_secret_key_that_is_at_least_32_bytes";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
        };
    });
builder.Services.AddAuthorization();

// Registering the Swagger Service
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "User API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter the JWT Token, for example: Bearer {token}",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new List<string>()
        }
    });
});

#endregion Services

var app = builder.Build();

#region middleware

// Error handling middleware
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{ \"error\": \"Internal Server Error.\" }");
    });
});

// Limit the number of simultaneous connections middleware
var maxConcurrentRequests = 100;
var semaphore = new SemaphoreSlim(maxConcurrentRequests);

app.Use(async (context, next) =>
{
    if (!await semaphore.WaitAsync(TimeSpan.FromSeconds(10)))
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsync("Service Unavailable: Too many concurrent requests.");
        return;
    }

    try
    {
        await next();
    }
    finally
    {
        semaphore.Release();
    }
});

// Enable Authentication
app.UseAuthentication();

// Enable HTTP request logging middleware
app.Use(async (context, next) =>
{
    Log.Information("HTTP {Method} {Path} requested", context.Request.Method, context.Request.Path);
    await next();
    Log.Information("HTTP {Method} {Path} responded with {StatusCode}", context.Request.Method, context.Request.Path, context.Response.StatusCode);
});

// Enable authorization
app.UseAuthorization();

// Enable Swagger UI
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "User API v1"));
}

// Initialize Database and add Seed Data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
    db.Database.EnsureCreated();
    seeder.Seed(db);
}

#endregion middleware

# region CURD API

// JWT Login endpoint
app.MapPost("/auth/login", (UserLogin userLogin) =>
{
    if (userLogin.Username == "testuser" && userLogin.Password == "password123")
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(secretKey);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, userLogin.Username) }),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return Results.Ok(new { token = tokenHandler.WriteToken(token) });
    }
    return Results.Unauthorized();
}).WithTags("Auth");

// GET: Get all users or a specific user
app.MapGet("/users", async (AppDbContext db, int? id) =>
{
    if (id.HasValue)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id.Value);
        return user is not null ? Results.Ok(user) : Results.NotFound(new { error = "User not found." });
    }
    return Results.Ok(await db.Users.AsNoTracking().ToListAsync());
}).WithTags("Users").RequireAuthorization();

// POST: Add new user
app.MapPost("/users", async (AppDbContext db, User user) =>
{
    if (string.IsNullOrWhiteSpace(user.Name) || !new EmailAddressAttribute().IsValid(user.Email))
    {
        return Results.BadRequest(new { error = "Invalid user data." });
    }
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Created($"/users/{user.Id}", user);
}).WithTags("Users").RequireAuthorization();

// PUT: Update User
app.MapPut("/users/{id}", async (AppDbContext db, int id, User updatedUser) =>
{
    if (string.IsNullOrWhiteSpace(updatedUser.Name) || !new EmailAddressAttribute().IsValid(updatedUser.Email))
    {
        return Results.BadRequest(new { error = "Invalid user data." });
    }
    var user = await db.Users.FindAsync(id);
    if (user is null) return Results.NotFound(new { error = "User not found." });

    user.Name = updatedUser.Name;
    user.Email = updatedUser.Email;
    await db.SaveChangesAsync();

    return Results.Ok(user);
}).WithTags("Users").RequireAuthorization();

// DELETE: Deleting a User
app.MapDelete("/users/{id}", async (AppDbContext db, int id) =>
{
    var user = await db.Users.FindAsync(id);
    if (user is null) return Results.NotFound(new { error = "User not found." });

    db.Users.Remove(user);
    await db.SaveChangesAsync();

    return Results.NoContent();
}).WithTags("Users").RequireAuthorization();

#endregion CURD API

app.Run();

