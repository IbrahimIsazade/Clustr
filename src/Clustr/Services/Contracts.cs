namespace Clustr.Services;

public record Credentials(string Email, string Password);
public record WorkspaceInput(string Name);
public record MemberInput(string Email, string Role);
public record ProductInput(string Name, string Sku, string Category, long PriceCents, int LowStockThreshold);
public record Adjustment(int Delta, string Reason);
public record CustomerInput(string Name, string Email);
public record CartLine(int ProductId, int Quantity);
public record Checkout(string RequestKey, int? CustomerId, string PaymentMethod, List<CartLine> Lines);
public record RefundInput(string Reason, bool Restock);
public class BusinessError(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
public static class Rules
{
    public static void Require(bool condition, string message, int status = 400)
    {
        if (!condition) throw new BusinessError(status, message);
    }
    public static string Text(string? value, string label, int max = 100)
    {
        var result = value?.Trim() ?? "";
        Require(result.Length > 0 && result.Length <= max, $"{label} must contain 1–{max} characters.");
        return result;
    }
}
