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
builder.Services.AddSingleton<LumiSense.Services.DeliveryPointsService>();
builder.Services.AddScoped<LumiSense.Services.ProfileImageStorage>();

var dbProvider = (builder.Configuration["Database:Provider"] ?? "auto").Trim().ToLowerInvariant();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (dbProvider == "auto" || dbProvider == "sqlserver" || dbProvider == "mssql")
    {
        var rawSqlConn = builder.Configuration.GetConnectionString("SqlServerConnection");
        var sqlConn = ResolveSqlServerConnectionString(builder.Configuration);

        if (!string.IsNullOrWhiteSpace(rawSqlConn) &&
            rawSqlConn.Contains("Password=CHANGE_ME", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(builder.Configuration["MSSQL_SA_PASSWORD"]) &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD")) &&
            string.IsNullOrWhiteSpace(builder.Configuration["SqlServer:SaPassword"]) &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SqlServer__SaPassword")))
        {
            if (dbProvider == "sqlserver" || dbProvider == "mssql")
            {
                throw new InvalidOperationException(
                    "SQL Server is selected but the connection string still contains Password=CHANGE_ME. " +
                    "Set the environment variable MSSQL_SA_PASSWORD (or set SqlServer:SaPassword) " +
                    "or update ConnectionStrings:SqlServerConnection with the real password.");
            }

            // auto mode: ignore placeholder and fall through
            sqlConn = null;
        }

        var (sqlOk, sqlErr) = TryConnectToSqlServer(sqlConn);
        if (!string.IsNullOrWhiteSpace(sqlConn) && sqlOk)
        {
            options.UseSqlServer(sqlConn);
            return;
        }

        if (dbProvider == "sqlserver" || dbProvider == "mssql")
        {
            var safe = SafeSqlServerConnectionSummary(builder.Configuration.GetConnectionString("SqlServerConnection"));
            var hint = string.IsNullOrWhiteSpace(sqlErr) ? "Unknown error." : sqlErr;
            throw new InvalidOperationException(
                "SQL Server is selected but the connection failed.\n" +
                $"Connection: {safe}\n" +
                $"Error: {hint}\n" +
                "Fix: set MSSQL_SA_PASSWORD (or update ConnectionStrings:SqlServerConnection), or switch Database:Provider back to 'auto'."
            );
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
.AddRoles<IdentityRole>()
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

    // Seed Admin role + default Admin user (override via env vars).
    await SeedAdminAsync(scope.ServiceProvider, app.Configuration, app.Environment);

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

static async Task SeedAdminAsync(IServiceProvider services, IConfiguration config, IHostEnvironment env)
{
    // Defaults are development-friendly; override via env vars in real deployments:
    // - LUMISENSE_ADMIN_EMAIL
    // - LUMISENSE_ADMIN_PASSWORD
    var adminEmail =
        (Environment.GetEnvironmentVariable("LUMISENSE_ADMIN_EMAIL") ??
         config["AdminSeed:Email"] ??
         "admin@lumisense.local").Trim();

    var adminPassword =
        (Environment.GetEnvironmentVariable("LUMISENSE_ADMIN_PASSWORD") ??
         config["AdminSeed:Password"] ??
         string.Empty).Trim();

    if (string.IsNullOrWhiteSpace(adminPassword))
    {
        // Only auto-seed a simple password in Development to avoid surprises in production.
        if (env.IsDevelopment())
        {
            adminPassword = "Admin123!";
        }
        else
        {
            return;
        }
    }

    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<IdentityUser>>();

    const string adminRole = "Admin";
    if (!await roleManager.RoleExistsAsync(adminRole))
    {
        await roleManager.CreateAsync(new IdentityRole(adminRole));
    }

    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if (adminUser is null)
    {
        adminUser = new IdentityUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };

        var created = await userManager.CreateAsync(adminUser, adminPassword);
        if (!created.Succeeded)
        {
            var msg = string.Join("; ", created.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Admin seed failed: {msg}");
        }
    }

    if (!await userManager.IsInRoleAsync(adminUser, adminRole))
    {
        await userManager.AddToRoleAsync(adminUser, adminRole);
    }
}

static void EnsureSqliteSchema(ApplicationDbContext dbContext)
{
    if (!dbContext.Database.IsSqlite())
    {
        return;
    }

    EnsureSqliteTableExists(
        dbContext,
        tableName: "ProductReviews",
        createSql: """
CREATE TABLE IF NOT EXISTS "ProductReviews" (
  "Id" INTEGER NOT NULL CONSTRAINT "PK_ProductReviews" PRIMARY KEY AUTOINCREMENT,
  "ProductId" INTEGER NOT NULL,
  "UserId" TEXT NOT NULL,
  "UserDisplayName" TEXT NULL,
  "Text" TEXT NOT NULL,
  "Rating" INTEGER NOT NULL DEFAULT 5,
  "CreatedAtUtc" TEXT NOT NULL,
  "IsApproved" INTEGER NOT NULL DEFAULT 0,
  CONSTRAINT "FK_ProductReviews_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_ProductReviews_ProductId" ON "ProductReviews" ("ProductId");
CREATE INDEX IF NOT EXISTS "IX_ProductReviews_UserId" ON "ProductReviews" ("UserId");
""");

    EnsureSqliteTableExists(
        dbContext,
        tableName: "ProductReviewReports",
        createSql: """
CREATE TABLE IF NOT EXISTS "ProductReviewReports" (
  "Id" INTEGER NOT NULL CONSTRAINT "PK_ProductReviewReports" PRIMARY KEY AUTOINCREMENT,
  "ReviewId" INTEGER NOT NULL,
  "ReporterUserId" TEXT NOT NULL,
  "Reason" TEXT NULL,
  "CreatedAtUtc" TEXT NOT NULL,
  "Resolved" INTEGER NOT NULL DEFAULT 0,
  CONSTRAINT "FK_ProductReviewReports_ProductReviews_ReviewId" FOREIGN KEY ("ReviewId") REFERENCES "ProductReviews" ("Id") ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS "IX_ProductReviewReports_ReviewId" ON "ProductReviewReports" ("ReviewId");
CREATE INDEX IF NOT EXISTS "IX_ProductReviewReports_ReporterUserId" ON "ProductReviewReports" ("ReporterUserId");
""");

    EnsureSqliteTableExists(
        dbContext,
        tableName: "UserProfiles",
        createSql: """
CREATE TABLE IF NOT EXISTS "UserProfiles" (
  "UserId" TEXT NOT NULL CONSTRAINT "PK_UserProfiles" PRIMARY KEY,
  "ProfileImagePath" TEXT NULL,
  "UpdatedAtUtc" TEXT NOT NULL
);
""");

    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "UserId", columnDefinitionSql: "TEXT NULL");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "CourierInfo", columnDefinitionSql: "TEXT NULL");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "PhoneNumber", columnDefinitionSql: "TEXT NULL");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "Status", columnDefinitionSql: "TEXT NOT NULL DEFAULT 'Pending'");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "DeliveryMethod", columnDefinitionSql: "TEXT NOT NULL DEFAULT 'office'");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "DeliveryCompany", columnDefinitionSql: "TEXT NULL");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "DeliveryPointName", columnDefinitionSql: "TEXT NULL");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "DeliveryPointType", columnDefinitionSql: "TEXT NULL");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "DeliveryPointCity", columnDefinitionSql: "TEXT NULL");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "DeliveryPointAddress", columnDefinitionSql: "TEXT NULL");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "DeliveryPointLat", columnDefinitionSql: "REAL NULL");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "DeliveryPointLng", columnDefinitionSql: "REAL NULL");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "AddressCity", columnDefinitionSql: "TEXT NULL");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "AddressLine1", columnDefinitionSql: "TEXT NULL");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "AddressLine2", columnDefinitionSql: "TEXT NULL");
    EnsureSqliteColumnExists(dbContext, tableName: "Orders", columnName: "AddressPostCode", columnDefinitionSql: "TEXT NULL");

    // Reviews schema evolution
    EnsureSqliteColumnExists(dbContext, tableName: "ProductReviews", columnName: "Rating", columnDefinitionSql: "INTEGER NOT NULL DEFAULT 5");
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
    if (!(string.Equals(tableName, "Orders", StringComparison.Ordinal) ||
          string.Equals(tableName, "ProductReviews", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("Unexpected schema patch request.");
    }

    if (string.Equals(tableName, "Orders", StringComparison.Ordinal) &&
        !(string.Equals(columnName, "UserId", StringComparison.Ordinal) ||
          string.Equals(columnName, "CourierInfo", StringComparison.Ordinal) ||
          string.Equals(columnName, "PhoneNumber", StringComparison.Ordinal) ||
          string.Equals(columnName, "Status", StringComparison.Ordinal) ||
          string.Equals(columnName, "DeliveryMethod", StringComparison.Ordinal) ||
          string.Equals(columnName, "DeliveryCompany", StringComparison.Ordinal) ||
          string.Equals(columnName, "DeliveryPointName", StringComparison.Ordinal) ||
          string.Equals(columnName, "DeliveryPointType", StringComparison.Ordinal) ||
          string.Equals(columnName, "DeliveryPointCity", StringComparison.Ordinal) ||
          string.Equals(columnName, "DeliveryPointAddress", StringComparison.Ordinal) ||
          string.Equals(columnName, "DeliveryPointLat", StringComparison.Ordinal) ||
          string.Equals(columnName, "DeliveryPointLng", StringComparison.Ordinal) ||
          string.Equals(columnName, "AddressCity", StringComparison.Ordinal) ||
          string.Equals(columnName, "AddressLine1", StringComparison.Ordinal) ||
          string.Equals(columnName, "AddressLine2", StringComparison.Ordinal) ||
          string.Equals(columnName, "AddressPostCode", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("Unexpected schema patch request.");
    }

    if (string.Equals(tableName, "ProductReviews", StringComparison.Ordinal) &&
        !string.Equals(columnName, "Rating", StringComparison.Ordinal))
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

static void EnsureSqliteTableExists(ApplicationDbContext dbContext, string tableName, string createSql)
{
    using var connection = dbContext.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
    {
        connection.Open();
    }

    using var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=$name;";
    var p = cmd.CreateParameter();
    p.ParameterName = "$name";
    p.Value = tableName;
    cmd.Parameters.Add(p);

    var exists = cmd.ExecuteScalar() is not null;
    if (exists) return;

    dbContext.Database.ExecuteSqlRaw(createSql);
}

static (bool ok, string? error) TryConnectToSqlServer(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return (false, "Connection string is empty.");
    }

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
        return (true, null);
    }
    catch (Exception ex)
    {
        return (false, ex.Message);
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

static string? ResolveSqlServerConnectionString(IConfiguration config)
{
    var sqlConn = config.GetConnectionString("SqlServerConnection");
    if (string.IsNullOrWhiteSpace(sqlConn)) return sqlConn;

    // Allow keeping the connection string in source control while supplying the secret password
    // via environment/config. Example:
    //   export MSSQL_SA_PASSWORD='YourStrong!Passw0rd'
    // and keep Password=CHANGE_ME in appsettings.json.
    const string marker = "Password=CHANGE_ME";
    if (!sqlConn.Contains(marker, StringComparison.OrdinalIgnoreCase))
    {
        return sqlConn;
    }

    var password =
        config["MSSQL_SA_PASSWORD"]
        ?? Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD")
        ?? config["SqlServer:SaPassword"]
        ?? Environment.GetEnvironmentVariable("SqlServer__SaPassword");

    if (string.IsNullOrWhiteSpace(password))
    {
        return sqlConn;
    }

    return sqlConn.Replace("Password=CHANGE_ME", $"Password={password}", StringComparison.OrdinalIgnoreCase);
}

static string SafeSqlServerConnectionSummary(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString)) return "<missing>";
    try
    {
        var b = new SqlConnectionStringBuilder(connectionString);
        var server = string.IsNullOrWhiteSpace(b.DataSource) ? "<unknown-server>" : b.DataSource;
        var db = string.IsNullOrWhiteSpace(b.InitialCatalog) ? "<default-db>" : b.InitialCatalog;
        var user = string.IsNullOrWhiteSpace(b.UserID) ? "<integrated>" : b.UserID;
        return $"{server};Database={db};User={user};Password=***";
    }
    catch
    {
        // Avoid echoing secrets; return a generic indicator.
        return "<invalid-connection-string>";
    }
}