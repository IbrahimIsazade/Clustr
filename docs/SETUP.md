# Run Clustr, one small step at a time

Think of Clustr as a little shop inside your computer. The browser is the shop window. The .NET application is the person behind the counter. The database is the notebook that remembers everything.

You do not need to install Node.js, SQL Server, PostgreSQL, or a separate database program.

## 1. Install the one thing you need

1. Visit [Microsoft's .NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
2. Find **SDK**, then choose the installer for your computer. On most Windows computers, that is **Windows x64**.
3. Run the installer and finish the steps. Choose the SDK, not only the Runtime: the SDK includes the tools that build the application.
4. Close any terminal windows and open a new PowerShell window.
5. Type this and press Enter:

```powershell
dotnet --list-sdks
```

You should see a line beginning with `10.0.`. Other versions can also be installed. Clustr's `global.json` selects a .NET 10 SDK.

## 2. Open the project folder

The folder you want contains `README.md`, `Clustr.slnx`, and `src`.

On this computer, paste this into PowerShell:

```powershell
cd "C:\Users\ibrah\Documents\Porfolio\Clustr"
```

`cd` means “go into this folder.” If you move the project, change the path to its new home. Keep the quotation marks if the path has spaces.

## 3. Collect the building blocks

Paste this and press Enter:

```powershell
dotnet restore Clustr.slnx
```

This downloads the small software packages Clustr uses. You need internet access for the first restore. Wait until the command finishes and the prompt comes back.

## 4. Switch Clustr on

```powershell
dotnet run --project src/Clustr --urls http://localhost:5080
```

Wait for a message like `Now listening on: http://localhost:5080`.

Leave this window open. The application is running inside it. It is normal that the prompt does not return while Clustr is running.

## 5. Open the shop window

Open your browser. Put this in the address bar:

```text
http://localhost:5080
```

`localhost` means “this computer.” You have not published anything to the internet.

1. Choose **Create an account**.
2. Enter an email address and a password with at least 10 characters. This edition does not send email, so no verification email arrives.
3. Choose **Create account**.
4. Choose **Create workspace**.
5. Give your business a name, such as **Fern & Folk Café**.
6. Press **Save**.

A workspace is a separate box for one business. A second workspace gets its own products, customers, stock, and sales.

## 6. Make your first sale in two minutes

1. Click **Products** on the left.
2. Click **Load sample café**. This adds six products, opening stock, and a sample customer. It only works in a workspace that has never had products.
3. Click **Point of sale**.
4. Click **Flat white**, then **Almond croissant**.
5. Look at the order on the right. Use the + and − buttons to change quantities.
6. Choose the sample customer, or leave **Walk-in customer**.
7. Leave payment as **Cash**.
8. Click **Complete sale**.
9. A receipt appears. Click **Print / Save PDF** if you want a copy. Choose **Save as PDF** in your browser's print window, or close the receipt.
10. Open **Products**. The quantities went down.
11. Open **Inventory**. You can see the stock changes.
12. Open **Overview**. You can see the sale total and chart.

No money actually moves. The card option is also a simulation. There is no card-number form.

## 7. Add your own product

1. Open **Products → Add product**.
2. Name: `Chocolate cookie`.
3. SKU: `COOKIE-001`. A SKU is a short product code. Each workspace must use a different code for each product, including archived products.
4. Category: `Bakery`.
5. Price: `2.50`. The form uses euros. Internally this is stored as 250 cents, avoiding rounding errors from floating-point money.
6. Low-stock threshold: `5`. That means “show a reminder when five or fewer remain.”
7. Save. New products start with zero stock.
8. Click **Adjust stock** on the product row.
9. Enter `20` and reason `Opening stock`. Save.

To remove two damaged cookies later, adjust by `-2` with the reason `Damaged`. Clustr will not allow stock below zero.

## 8. Refund a sale

1. Open **Sales**.
2. Find the sale and click **Refund**.
3. Explain why, such as `Customer returned the order`.
4. Leave the restock box checked only if the products can be sold again. Uncheck it for used or damaged products.
5. Save.

This edition refunds the whole sale. You cannot refund the same sale twice. The receipt stays in history, and the dashboard removes that sale from net sales.

## 9. Let another person help

1. Ask them to create their own account. For testing by yourself, use a private/incognito browser window for the second account.
2. In your owner account, open **Team → Add teammate**.
3. Enter the email they registered with.
4. Choose **Cashier** or **Manager** and save.
5. Ask them to reload their page. Their workspace list will update.

Cashiers can view products and inventory, add customers, complete sales, and see receipts. Managers can also change products/stock, refund sales, and view reports. Only the owner can add or remove teammates.

## 10. Stop it, then start it again

To stop: click the PowerShell window and press **Ctrl+C**.

To start again: open PowerShell in the project folder and run the command from step 4. You do not need to register again or reload sample data.

The notebook/database lives at:

```text
src/Clustr/App_Data/clustr.db
```

Do not delete it unless you deliberately want to erase every account and workspace. To make a backup, stop Clustr first and copy the entire `App_Data` folder somewhere safe. Restore by stopping the app and putting your backup folder back in the same place. Keep a copy of the replaced folder in case you need it later.

## 11. Check that the code works

In another terminal in the project folder:

```powershell
dotnet test tests/Clustr.Tests/Clustr.Tests.csproj
```

You should see a passing test summary. Tests use separate temporary databases and do not change your shop.

## If something goes wrong

| What you see | What to do |
| --- | --- |
| `dotnet is not recognized` | Install the .NET 10 SDK, close the terminal, then open a new one. |
| A compatible SDK cannot be found | Run `dotnet --list-sdks`. Install .NET 10 if it is missing. |
| NuGet cannot be reached | Check internet access and firewall/proxy settings, then repeat `dotnet restore Clustr.slnx`. |
| Address already in use | Stop another Clustr window, or run with `--urls http://localhost:5081` and open that address instead. |
| Browser says it cannot connect | Keep the terminal running and wait for “Now listening.” Check the port in both places matches. |
| Empty product list | Load the sample café, or add a product and adjust its stock. |
| Not enough stock | Reduce cart quantity or have a manager add stock. Return to POS to refresh the product list. |
| Checkout result is uncertain | Press **Retry same checkout**. Keep the same browser tab; pending checkout is preserved on reload when session storage is available. |
| Too many login attempts | Wait one minute and try again. |
| Teammate not found | They must register first, using the exact email you enter. |
| “No such table/column” after editing database models | This starter creates its schema automatically, but does not upgrade old schemas. See the migration note in ARCHITECTURE.md. Back up data before making schema changes. |
| Build stalls in a restricted environment | Try `dotnet build src/Clustr -p:UseSharedCompilation=false`. |

If a sale is uncertain and the original tab/session storage is gone, check **Sales** before making a new checkout. Server-side retry protection needs the original request key.

## Optional: run with Docker instead

Use this path only if Docker Desktop is already installed and running with Linux containers. You do not need a local .NET SDK for this route.

```powershell
docker compose up --build
```

Wait for startup, then open `http://localhost:5080`. To stop, press Ctrl+C and run `docker compose down`. The named data volume remains. Do not add `-v` to that command unless you intend to delete the Docker database.

Docker and normal .NET startup have separate databases. Accounts created in one do not appear in the other.
