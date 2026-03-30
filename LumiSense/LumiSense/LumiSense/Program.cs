using Microsoft.EntityFrameworkCore;
using LumiSense.Data;
using LumiSense.Models;
using Pomelo.EntityFrameworkCore.MySql;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using System.Globalization;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// Add services
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromDays(7);
});

builder.Services
    .AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

builder.Services
    .AddRazorPages()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

builder.Services.AddScoped<LumiSense.Services.CartSessionService>();

var dbProvider = (builder.Configuration["Database:Provider"] ?? "auto").Trim().ToLowerInvariant();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (dbProvider == "auto" || dbProvider == "sqlserver" || dbProvider == "mssql")
    {
        var sqlConn = builder.Configuration.GetConnectionString("SqlServerConnection");
        if (!string.IsNullOrWhiteSpace(sqlConn) && CanConnectToSqlServer(sqlConn))
        {
            options.UseSqlServer(sqlConn);
            return;
        }

        if (dbProvider == "sqlserver" || dbProvider == "mssql")
        {
            throw new InvalidOperationException("SQL Server is selected but is not reachable or connection string is missing.");
        }
        // else: auto fall-through to MySQL/SQLite
    }

    if (dbProvider == "mysql")
    {
        var mysqlConn = builder.Configuration.GetConnectionString("MySqlConnection")
            ?? builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing MySQL connection string.");

        var mysqlVersion = builder.Configuration["Database:MySqlServerVersion"] ?? "8.0.36-mysql";
        options.UseMySql(mysqlConn, ServerVersion.Parse(mysqlVersion));
    }
    else
    {
        var sqliteConn = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing SQLite connection string.");
        options.UseSqlite(sqliteConn);
    }
});

// Add Identity
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 4;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (IsAjaxOrJsonRequest(ctx.Request))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };

    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        if (IsAjaxOrJsonRequest(ctx.Request))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
});

var app = builder.Build();

// Configure pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

var supportedCultures = new[] { "en", "tr", "bg" }
    .Select(c => new CultureInfo(c))
    .ToList();

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("en"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseSession();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// Create database and seed data
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.EnsureCreated();
    EnsureSqliteSchema(dbContext);

    // Seed products if empty
    if (!dbContext.Products.Any())
    {
        dbContext.Products.AddRange(
            new Product
            {
                Name = "LumiSense UV Meter Pro",
                Description = "Arduino-powered precision UV meter with real-time LCD display",
                Price = 89.99m,
                Stock = 25,
                ImageIcon = "fa-microchip",
                Category = "Devices",
                IsPopular = true
            },
            new Product
            {
                Name = "UV Armor Bundle",
                Description = "Complete protection kit: SPF 50+ sunscreen, UV-protective shirt, and cooling towel",
                Price = 49.99m,
                Stock = 50,
                ImageIcon = "fa-sun",
                Category = "Accessories",
                IsPopular = true
            },
            new Product
            {
                Name = "UV Sensor Module",
                Description = "DIY UV sensor module compatible with Arduino and Raspberry Pi",
                Price = 24.99m,
                Stock = 100,
                ImageIcon = "fa-plug",
                Category = "Components",
                IsPopular = false
            },
            new Product
            {
                Name = "Beach Safety Kit",
                Description = "Premium kit: LumiSense Mini, UPF 50+ umbrella, SPF 50 sunscreen, and UV alert band",
                Price = 149.99m,
                Stock = 15,
                ImageIcon = "fa-umbrella-beach",
                Category = "Bundles",
                IsPopular = true
            },
            new Product
            {
                Name = "UV Protection Arm Sleeves",
                Description = "UPF 50+ cooling arm sleeves, perfect for beach sports",
                Price = 19.99m,
                Stock = 75,
                ImageIcon = "fa-hand-peace",
                Category = "Accessories",
                IsPopular = false
            }
        );
        dbContext.SaveChanges();
    }

    // Seed initial UV reading if empty
    if (!dbContext.UVReadings.Any())
    {
        dbContext.UVReadings.Add(new UVReading
        {
            Value = 4.5,
            Timestamp = DateTime.Now,
            Location = "Beach Station",
            DeviceId = "Arduino_UVMeter_001",
            SafetyStatus = "Moderate"
        });
        dbContext.SaveChanges();
    }
}

app.Run();

static void EnsureSqliteSchema(ApplicationDbContext dbContext)
{
    if (!dbContext.Database.IsSqlite())
    {
        return;
    }

    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "UserId", columnDefinitionSql: "TEXT NULL");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "CourierInfo", columnDefinitionSql: "TEXT NULL");
}

static void EnsureSqliteColumnExists(
    ApplicationDbContext dbContext,
    string tableName,
    string columnName,
    string columnDefinitionSql)
{
    using var connection = dbContext.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
    {
        connection.Open();
    }

    // This is a tiny schema patch for this app's known tables/columns.
    // Keep it whitelisted to avoid executing arbitrary SQL.
    if (!string.Equals(tableName, "Orders", StringComparison.Ordinal) ||
        !(string.Equals(columnName, "UserId", StringComparison.Ordinal) ||
          string.Equals(columnName, "CourierInfo", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("Unexpected schema patch request.");
    }

    using var cmd = connection.CreateCommand();
    cmd.CommandText = $"PRAGMA table_info('{tableName}');";
    using var reader = cmd.ExecuteReader();

    while (reader.Read())
    {
        // PRAGMA table_info returns: cid, name, type, notnull, dflt_value, pk
        var name = reader["name"]?.ToString();
        if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
    }

    var sql = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {columnDefinitionSql};";
    dbContext.Database.ExecuteSqlRaw(sql);
}

static bool CanConnectToSqlServer(string connectionString)
{
    try
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (builder.ConnectTimeout > 2)
        {
            builder.ConnectTimeout = 2;
        }

        using var conn = new SqlConnection(builder.ConnectionString);
        conn.Open();
        conn.Close();
        return true;
    }
    catch
    {
        return false;
    }
}

static bool IsAjaxOrJsonRequest(HttpRequest request)
{
    if (request.Headers.TryGetValue("X-Requested-With", out var xrw) &&
        string.Equals(xrw.ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (request.Headers.TryGetValue("Accept", out var accept) &&
        accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return false;
}