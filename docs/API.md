# Clustr API

Base path: `/api`. JSON property names use camelCase. Authentication is a browser cookie. All writes require the header `X-Clustr-Request: 1`, including registration and login. All business paths check membership in `{workspace}`.

## Routes

| Method | Path | Permission / result |
| --- | --- | --- |
| POST | `/auth/register` | Public; `{email,password}`; signs in |
| POST | `/auth/login` | Public; `{email,password}`; signs in |
| POST | `/auth/logout` | Signed in; clears cookie |
| GET | `/auth/me` | Signed in; user ID and email |
| GET | `/workspaces` | Current user's workspace IDs/names/roles |
| POST | `/workspaces` | Signed in; `{name}`; creates owner membership |
| GET | `/w/{workspace}/products?search=&page=1` | Member; `{items,total,page}`; 50 per page |
| POST | `/w/{workspace}/products` | Manager/owner; product input below |
| PUT | `/w/{workspace}/products/{id}` | Manager/owner; replaces editable product fields |
| DELETE | `/w/{workspace}/products/{id}` | Manager/owner; archives |
| POST | `/w/{workspace}/products/{id}/stock` | Manager/owner; `{delta,reason}` |
| GET | `/w/{workspace}/movements?page=1` | Member; newest 50 first |
| GET | `/w/{workspace}/customers` | Member; customer list |
| POST | `/w/{workspace}/customers` | Member; `{name,email}` |
| GET | `/w/{workspace}/sales?page=1` | Member; newest 50 sales and lines |
| POST | `/w/{workspace}/sales` | Member; checkout below |
| GET | `/w/{workspace}/sales/{id}` | Member; receipt data and original lines |
| POST | `/w/{workspace}/sales/{id}/refund` | Manager/owner; `{reason,restock}` |
| GET | `/w/{workspace}/overview` | Manager/owner; summary and low-stock list |
| GET | `/w/{workspace}/export` | Manager/owner; all sales CSV download |
| GET | `/w/{workspace}/members` | Manager/owner; member IDs/emails/roles |
| POST | `/w/{workspace}/members` | Owner; `{email,role}`; registered user only |
| DELETE | `/w/{workspace}/members/{id}` | Owner; removes a non-owner membership |
| POST | `/w/{workspace}/sample-data` | Manager/owner; requires no existing products |

`GET /health` is public and outside `/api`. API failures use HTTP status codes: 400 invalid input, 401 missing/invalid login, 403 forbidden, 404 not found in this workspace, 409 conflict, 429 auth rate limit, 500 unexpected error. Business errors return Problem Details with a readable `detail` field. Framework-level authentication/binding errors may have an empty body.

## Product input

```json
{
  "name": "Flat white",
  "sku": "COF-001",
  "category": "Coffee",
  "priceCents": 380,
  "lowStockThreshold": 5
}
```

Stock starts at zero. Use the stock endpoint with `{"delta":20,"reason":"Opening stock"}`. Updates never set stock directly.

## Checkout input

```json
{
  "requestKey": "25e7ca1d-2ab7-42bb-8268-75e547519909",
  "customerId": null,
  "paymentMethod": "Cash",
  "lines": [{ "productId": 1, "quantity": 2 }]
}
```

Generate a new UUID for each new checkout. Reuse the **same key and payload** when retrying an uncertain request. The other accepted payment method is `Card (simulated)`. No amount supplied by the client is trusted.

## Try it from PowerShell

Start Clustr first. Use a fresh email, or call `/auth/login` for an existing account.

```powershell
$headers = @{ 'X-Clustr-Request' = '1' }
$body = @{ email = 'learner@example.com'; password = 'My long practice password' } | ConvertTo-Json
Invoke-RestMethod http://localhost:5080/api/auth/register -Method Post -Headers $headers -ContentType 'application/json' -Body $body -SessionVariable shopSession

$body = @{ name = 'My practice shop' } | ConvertTo-Json
$workspace = Invoke-RestMethod http://localhost:5080/api/workspaces -Method Post -Headers $headers -ContentType 'application/json' -Body $body -WebSession $shopSession

Invoke-RestMethod "http://localhost:5080/api/w/$($workspace.id)/sample-data" -Method Post -Headers $headers -WebSession $shopSession
Invoke-RestMethod "http://localhost:5080/api/w/$($workspace.id)/products" -WebSession $shopSession
```

These commands create real demo records in your local database. The browser and this PowerShell session have separate sign-in cookies.
