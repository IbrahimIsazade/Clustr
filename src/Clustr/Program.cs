using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Clustr.Data;
using Clustr.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDataProtection().SetApplicationName("Clustr");
builder.Services.AddOptions<KeyManagementOptions>().Configure<IConfiguration, IWebHostEnvironment>((options, config, environment) =>
{
    var directory = config["DataDirectory"] ?? Path.Combine(environment.ContentRootPath, "App_Data");
    options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(Path.Combine(directory, "keys")), NullLoggerFactory.Instance);
});
builder.Services.AddDbContext<AppDb>((services, o) =>
{
    var dataPath = services.GetRequiredService<IConfiguration>()["DataDirectory"]
        ?? Path.Combine(services.GetRequiredService<IWebHostEnvironment>().ContentRootPath, "App_Data");
    Directory.CreateDirectory(dataPath);
    var connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataPath, "clustr.db"), DefaultTimeout = 10 };
    o.UseSqlite(connection.ToString());
});
builder.Services.AddScoped<CommerceService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "Clustr.Session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
}
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    try
    {
        // No CORS policy is enabled. This custom header makes cross-origin writes
        // require a preflight that our server rejects (including login CSRF).
        if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method))
            Rules.Require(context.Request.Headers["X-Clustr-Request"] == "1", "Missing request header.", 403);
        await next();
    }
    catch (BusinessError e) { await Results.Problem(e.Message, statusCode: e.Status).ExecuteAsync(context); }
    catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 19 })
    { await Results.Problem("That record already exists or violates a data rule.", statusCode: 409).ExecuteAsync(context); }
    catch (SqliteException e) when (e.SqliteErrorCode is 5 or 6)
    { await Results.Problem("The database is busy. Retry the same operation.", statusCode: 409).ExecuteAsync(context); }
    catch (Exception e) when (e is not BadHttpRequestException)
    {
        app.Logger.LogError(e, "Request failed: {TraceId}", context.TraceIdentifier);
        await Results.Problem("An unexpected error occurred. Check the server log.", statusCode: 500).ExecuteAsync(context);
    }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", application = "Clustr" }));

var auth = app.MapGroup("/api/auth");
auth.MapPost("/register", async (Credentials input, AppDb db, HttpContext c) =>
{
    var email = Email(input.Email);
    Rules.Require(input.Password is { Length: >= 10 and <= 128 }, "Use a password with 10–128 characters.");
    var user = new User { Email = email };
    user.PasswordHash = new PasswordHasher<User>().HashPassword(user, input.Password);
    db.Users.Add(user);
    await db.SaveChangesAsync();
    await SignIn(c, user);
    return Results.Ok(new { user.Id, user.Email });
}).RequireRateLimiting("auth");
auth.MapPost("/login", async (Credentials input, AppDb db, HttpContext c) =>
{
    var email = Email(input.Email);
    Rules.Require(input.Password is { Length: > 0 and <= 128 }, "Invalid email or password.", 401);
    var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email);
    Rules.Require(user != null && new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash, input.Password) != PasswordVerificationResult.Failed, "Invalid email or password.", 401);
    await SignIn(c, user!);
    return Results.Ok(new { user!.Id, user.Email });
}).RequireRateLimiting("auth");
auth.MapPost("/logout", async (HttpContext c) => { await c.SignOutAsync(); return Results.NoContent(); }).RequireAuthorization();
auth.MapGet("/me", (HttpContext c) => new { id = UserId(c), email = c.User.FindFirstValue(ClaimTypes.Email) }).RequireAuthorization();

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/workspaces", async (AppDb db, HttpContext c) => { var userId = UserId(c); return await (from m in db.Memberships join w in db.Workspaces on m.WorkspaceId equals w.Id where m.UserId == userId select new { w.Id, w.Name, m.Role }).ToListAsync(); });
api.MapPost("/workspaces", async (WorkspaceInput input, AppDb db, HttpContext c) =>
{
    await using var tx = await db.Database.BeginTransactionAsync();
    var w = new Workspace { Name = Rules.Text(input.Name, "Workspace name") };
    db.Workspaces.Add(w);
    await db.SaveChangesAsync();
    db.Memberships.Add(new Membership { WorkspaceId = w.Id, UserId = UserId(c), Role = "Owner" });
    await db.SaveChangesAsync();
    await tx.CommitAsync();
    return Results.Ok(w);
});

var tenant = api.MapGroup("/w/{workspace:int}");
tenant.AddEndpointFilter(async (ctx, next) =>
{
    var c = ctx.HttpContext;
    var workspace = int.Parse(c.Request.RouteValues["workspace"]!.ToString()!);
    var db = c.RequestServices.GetRequiredService<AppDb>();
    var userId = UserId(c);
    var member = await db.Memberships.SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.UserId == userId);
    Rules.Require(member != null, "You do not belong to this workspace.", 403);
    c.Items["Role"] = member!.Role;
    return await next(ctx);
});
tenant.MapGet("/members", async (int workspace, AppDb db, HttpContext c) =>
{
    Manage(c);
    return await (from m in db.Memberships join u in db.Users on m.UserId equals u.Id where m.WorkspaceId == workspace select new { m.Id, u.Email, m.Role }).ToListAsync();
});
tenant.MapPost("/members", async (int workspace, MemberInput input, AppDb db, HttpContext c) =>
{
    Rules.Require((string)c.Items["Role"]! == "Owner", "Only the owner can add staff.", 403);
    Rules.Require(input.Role is "Manager" or "Cashier", "Choose Manager or Cashier.");
    var email = Email(input.Email);
    var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email);
    Rules.Require(user != null, "Ask this person to register first.", 404);
    db.Memberships.Add(new Membership { WorkspaceId = workspace, UserId = user!.Id, Role = input.Role });
    await db.SaveChangesAsync();
    return Results.NoContent();
});
tenant.MapDelete("/members/{id:int}", async (int workspace, int id, AppDb db, HttpContext c) =>
{
    Rules.Require((string)c.Items["Role"]! == "Owner", "Only the owner can remove staff.", 403);
    var member = await db.Memberships.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspace && x.Role != "Owner");
    Rules.Require(member != null, "Staff member not found.", 404);
    db.Memberships.Remove(member!); await db.SaveChangesAsync(); return Results.NoContent();
});
tenant.MapGet("/products", async (int workspace, AppDb db, string? search, int page = 1) =>
{
    Rules.Require(page is > 0 and <= 100000, "Invalid page.");
    var q = db.Products.Where(x => x.WorkspaceId == workspace && x.Active);
    if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.Contains(search) || x.Sku.Contains(search) || x.Category.Contains(search));
    return Results.Ok(new { items = await q.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * 50).Take(50).ToListAsync(), total = await q.CountAsync(), page });
});
tenant.MapPost("/sample-data", async (int workspace, AppDb db, HttpContext c) =>
{
    Manage(c);
    await using var tx = await db.Database.BeginTransactionAsync();
    Rules.Require(!await db.Products.AnyAsync(x => x.WorkspaceId == workspace), "Sample data can only be added to a workspace with no products.", 409);
    var samples = new[] { ("Flat white", "COF-001", "Coffee", 380L, 45), ("Iced latte", "COF-002", "Coffee", 450L, 32), ("Almond croissant", "BAK-001", "Bakery", 320L, 4), ("Cinnamon bun", "BAK-002", "Bakery", 350L, 18), ("Matcha latte", "TEA-001", "Tea", 480L, 24), ("House blend 250g", "BEAN-001", "Retail", 1250L, 3) };
    foreach (var (name, sku, category, price, stock) in samples)
    {
        var p = new Product { WorkspaceId = workspace, Name = name, Sku = sku, Category = category, PriceCents = price, Stock = stock };
        db.Products.Add(p); await db.SaveChangesAsync();
        db.Movements.Add(new StockMovement { WorkspaceId = workspace, ProductId = p.Id, ProductName = p.Name, Delta = stock, Reason = "Sample opening stock", ActorId = UserId(c) });
    }
    db.Customers.Add(new Customer { WorkspaceId = workspace, Name = "Jamie Green (sample)", Email = "jamie@example.com" });
    await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.NoContent();
});
tenant.MapPost("/products", async (int workspace, ProductInput input, AppDb db, HttpContext c) =>
{
    Manage(c); var product = new Product { WorkspaceId = workspace }; SetProduct(product, input);
    db.Products.Add(product); await db.SaveChangesAsync(); return Results.Ok(product);
});
tenant.MapPut("/products/{id:int}", async (int workspace, int id, ProductInput input, AppDb db, HttpContext c) =>
{
    Manage(c); var p = await db.Products.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspace && x.Active);
    Rules.Require(p != null, "Product not found.", 404); SetProduct(p!, input); await db.SaveChangesAsync(); return Results.Ok(p);
});
tenant.MapDelete("/products/{id:int}", async (int workspace, int id, AppDb db, HttpContext c) =>
{
    Manage(c); var p = await db.Products.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspace && x.Active);
    Rules.Require(p != null, "Product not found.", 404); p!.Active = false; await db.SaveChangesAsync(); return Results.NoContent();
});
tenant.MapPost("/products/{id:int}/stock", async (int workspace, int id, Adjustment input, CommerceService service, HttpContext c) =>
{ Manage(c); await service.Adjust(workspace, UserId(c), id, input); return Results.NoContent(); });
tenant.MapGet("/movements", async (int workspace, AppDb db, int page = 1) =>
{ Rules.Require(page is > 0 and <= 100000, "Invalid page."); return await db.Movements.Where(x => x.WorkspaceId == workspace).OrderByDescending(x => x.Id).Skip((page - 1) * 50).Take(50).ToListAsync(); });
tenant.MapGet("/customers", async (int workspace, AppDb db) => await db.Customers.Where(x => x.WorkspaceId == workspace).OrderBy(x => x.Name).ToListAsync());
tenant.MapPost("/customers", async (int workspace, CustomerInput input, AppDb db) =>
{
    var customer = new Customer { WorkspaceId = workspace, Name = Rules.Text(input.Name, "Customer name"), Email = Email(input.Email) };
    db.Customers.Add(customer); await db.SaveChangesAsync(); return Results.Ok(customer);
});
tenant.MapPost("/sales", async (int workspace, Checkout input, CommerceService service, HttpContext c) => Results.Ok(await service.Checkout(workspace, UserId(c), input)));
tenant.MapGet("/sales", async (int workspace, AppDb db, int page = 1) =>
{ Rules.Require(page is > 0 and <= 100000, "Invalid page."); return await db.Sales.Include(x => x.Lines).Where(x => x.WorkspaceId == workspace).OrderByDescending(x => x.Id).Skip((page - 1) * 50).Take(50).ToListAsync(); });
tenant.MapGet("/sales/{id:int}", async (int workspace, int id, AppDb db) =>
    await db.Sales.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspace) is { } sale ? Results.Ok(sale) : Results.NotFound());
tenant.MapPost("/sales/{id:int}/refund", async (int workspace, int id, RefundInput input, CommerceService service, HttpContext c) =>
{ Manage(c); await service.Refund(workspace, UserId(c), id, input); return Results.NoContent(); });
tenant.MapGet("/overview", async (int workspace, AppDb db, HttpContext c) =>
{
    Manage(c);
    var sales = await db.Sales.Where(x => x.WorkspaceId == workspace).ToListAsync();
    var products = await db.Products.Where(x => x.WorkspaceId == workspace && x.Active).ToListAsync();
    var since = DateTime.UtcNow.Date.AddDays(-6);
    return Results.Ok(new {
        revenueCents = sales.Where(x => !x.Refunded).Sum(x => x.TotalCents),
        salesCount = sales.Count(x => !x.Refunded), refundCents = sales.Where(x => x.Refunded).Sum(x => x.TotalCents),
        stockValueCents = products.Sum(x => x.PriceCents * x.Stock),
        lowStock = products.Where(x => x.Stock <= x.LowStockThreshold),
        days = Enumerable.Range(0, 7).Select(i => new { date = since.AddDays(i).ToString("yyyy-MM-dd"), total = sales.Where(x => !x.Refunded && x.CreatedAt.Date == since.AddDays(i)).Sum(x => x.TotalCents) })
    });
});
tenant.MapGet("/export", async (int workspace, AppDb db, HttpContext c) =>
{
    Manage(c); var sales = await db.Sales.Where(x => x.WorkspaceId == workspace).OrderBy(x => x.Id).ToListAsync();
    var csv = "Sale,Created UTC,Total EUR,Status\n" + string.Join("\n", sales.Select(s => $"{s.Id},{s.CreatedAt:O},{(s.TotalCents / 100m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},{(s.Refunded ? "Refunded" : "Completed")}"));
    return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv", "clustr-sales.csv");
});
app.Run();

static int UserId(HttpContext c) => int.Parse(c.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
static void Manage(HttpContext c) => Rules.Require((string)c.Items["Role"]! is "Owner" or "Manager", "A manager or owner role is required.", 403);
static string Email(string? value)
{
    var email = Rules.Text(value, "Email", 254).ToLowerInvariant();
    Rules.Require(MailAddress.TryCreate(email, out var parsed) && parsed.Address == email, "Enter a valid email address.");
    return email;
}
static Task SignIn(HttpContext c, User user) => c.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Email, user.Email)], CookieAuthenticationDefaults.AuthenticationScheme)));
static void SetProduct(Product p, ProductInput input)
{
    p.Name = Rules.Text(input.Name, "Product name"); p.Sku = Rules.Text(input.Sku, "SKU", 40).ToUpperInvariant();
    p.Category = Rules.Text(input.Category, "Category", 60);
    Rules.Require(input.PriceCents is >= 0 and <= 100000000, "Price must be between €0 and €1,000,000.");
    Rules.Require(input.LowStockThreshold is >= 0 and <= 1000000, "Invalid low-stock threshold.");
    p.PriceCents = input.PriceCents; p.LowStockThreshold = input.LowStockThreshold;
}

public partial class Program;
