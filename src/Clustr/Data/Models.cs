namespace Clustr.Data;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}
public class Workspace
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
public class Membership
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public int UserId { get; set; }
    public string Role { get; set; } = "Cashier";
}
public class Product
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Category { get; set; } = "";
    public long PriceCents { get; set; }
    public int Stock { get; set; }
    public int LowStockThreshold { get; set; } = 5;
    public bool Active { get; set; } = true;
}
public class Customer
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}
public class Sale
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public int? CustomerId { get; set; }
    public string RequestKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long TotalCents { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public bool Refunded { get; set; }
    public string? RefundReason { get; set; }
    public DateTime? RefundedAt { get; set; }
    public List<SaleLine> Lines { get; set; } = [];
}
public class SaleLine
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public int ProductId { get; set; }
    public string Name { get; set; } = "";
    public long UnitPriceCents { get; set; }
    public int Quantity { get; set; }
}
public class StockMovement
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int Delta { get; set; }
    public string Reason { get; set; } = "";
    public int ActorId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
