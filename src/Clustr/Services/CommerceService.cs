using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clustr.Data;
using Microsoft.EntityFrameworkCore;

namespace Clustr.Services;

// SQLite serializes writers across connections. Each operation starts its transaction
// before reading stock, so stock, sale and movement records succeed or fail together.
public class CommerceService(AppDb db)
{
    public async Task<Sale> Checkout(int workspace, int actor, Checkout input)
    {
        Rules.Require(Guid.TryParse(input.RequestKey, out _), "Checkout requires a UUID request key.");
        Rules.Require(input.Lines is { Count: > 0 and <= 100 }, "Add 1–100 cart lines.");
        Rules.Require(input.PaymentMethod is "Cash" or "Card (simulated)", "Choose a supported payment method.");
        Rules.Require(input.Lines.All(x => x.Quantity is > 0 and <= 10000), "Quantities must be between 1 and 10000.");
        Rules.Require(input.Lines.Select(x => x.ProductId).Distinct().Count() == input.Lines.Count, "Combine duplicate products into one line.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { input.CustomerId, input.PaymentMethod, Lines = input.Lines.OrderBy(x => x.ProductId) }))));
        await using var tx = await db.Database.BeginTransactionAsync();
        var existing = await db.Sales.Include(x => x.Lines).SingleOrDefaultAsync(x => x.WorkspaceId == workspace && x.RequestKey == input.RequestKey);
        if (existing != null)
        {
            Rules.Require(existing.RequestHash == hash, "This request key was already used for a different cart.", 409);
            return existing;
        }
        if (input.CustomerId != null)
            Rules.Require(await db.Customers.AnyAsync(x => x.Id == input.CustomerId && x.WorkspaceId == workspace), "Customer not found.", 404);
        var sale = new Sale { WorkspaceId = workspace, CustomerId = input.CustomerId, RequestKey = input.RequestKey, RequestHash = hash, PaymentMethod = input.PaymentMethod };
        foreach (var line in input.Lines.OrderBy(x => x.ProductId))
        {
            var product = await db.Products.SingleOrDefaultAsync(x => x.Id == line.ProductId && x.WorkspaceId == workspace && x.Active);
            Rules.Require(product != null, "Product not found.", 404);
            Rules.Require(product!.Stock >= line.Quantity, $"Not enough stock for {product.Name}.", 409);
            product.Stock -= line.Quantity;
            sale.Lines.Add(new SaleLine { ProductId = product.Id, Name = product.Name, UnitPriceCents = product.PriceCents, Quantity = line.Quantity });
            sale.TotalCents += product.PriceCents * line.Quantity;
            Movement(product, -line.Quantity, "Sale", actor);
        }
        db.Sales.Add(sale);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return sale;
    }

    public async Task Refund(int workspace, int actor, int saleId, RefundInput input)
    {
        var reason = Rules.Text(input.Reason, "Refund reason", 300);
        await using var tx = await db.Database.BeginTransactionAsync();
        var sale = await db.Sales.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == saleId && x.WorkspaceId == workspace);
        Rules.Require(sale != null, "Sale not found.", 404);
        Rules.Require(!sale!.Refunded, "This sale has already been refunded.", 409);
        if (input.Restock)
            foreach (var line in sale.Lines)
            {
                var product = await db.Products.SingleAsync(x => x.Id == line.ProductId && x.WorkspaceId == workspace);
                Rules.Require(product.Stock <= 1000000 - line.Quantity, "Stock limit would be exceeded.", 409);
                product.Stock += line.Quantity;
                Movement(product, line.Quantity, $"Refund #{sale.Id}: {reason}", actor);
            }
        sale.Refunded = true;
        sale.RefundReason = reason;
        sale.RefundedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task Adjust(int workspace, int actor, int productId, Adjustment input)
    {
        var reason = Rules.Text(input.Reason, "Adjustment reason", 300);
        Rules.Require(input.Delta is >= -1000000 and <= 1000000 && input.Delta != 0, "Enter a nonzero adjustment up to 1,000,000 units.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var product = await db.Products.SingleOrDefaultAsync(x => x.Id == productId && x.WorkspaceId == workspace && x.Active);
        Rules.Require(product != null, "Product not found.", 404);
        Rules.Require((long)product!.Stock + input.Delta is >= 0 and <= 1000000, "Stock must stay between 0 and 1,000,000.", 409);
        product.Stock += input.Delta;
        Movement(product, input.Delta, reason, actor);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private void Movement(Product p, int delta, string reason, int actor) => db.Movements.Add(new StockMovement
    { WorkspaceId = p.WorkspaceId, ProductId = p.Id, ProductName = p.Name, Delta = delta, Reason = reason, ActorId = actor });
}
