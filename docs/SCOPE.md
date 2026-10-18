# The mid-level scope

The supplied blueprint described an enterprise platform. This implementation preserves the useful business journey and reduces the number of moving parts. Its name is **Clustr** throughout.

| Area | Implemented | Deliberately deferred |
| --- | --- | --- |
| Accounts | Register, login/logout, password hashing, cookie sessions, auth rate limits | Email verification, password reset, MFA, OAuth, session management |
| Businesses | Create/switch workspaces, scoped data, owner/manager/cashier memberships, remove staff | Emailed invitations, custom permissions, branches, workspace editing |
| Catalog | Products, category text, unique SKU, search, pagination, edit, archive | Variants, barcodes/scanning, image uploads, category administration |
| Inventory | Single stock location, adjustments, immutable movement history, low-stock overview | Transfers, reservations, purchasing, multiple warehouses |
| Sales | Cart, transactional stock deductions, idempotent checkout, snapshot lines | Discounts, tax engine, real payment provider, partial refunds |
| Refunds | Full refund, reason, timestamp, optional restock, duplicate protection | Payment gateway refunds, separate financial ledger |
| Customers | Create/list and associate with sales | Editing, segmentation, loyalty, dedicated purchase-history page |
| Reports | All-time net sales, refunded amount, retail stock value, UTC daily chart, CSV | Scheduled reports, profit/cost accounting, top-product/customer reports |
| Receipts | Printable receipt, browser Save as PDF | Server PDF engine, tax invoices, email delivery |
| Alerts | Current low-stock list | Persistent notifications, emails, SignalR, delivery retries |
| Engineering | .NET 10, EF Core, SQLite, HTML/CSS/JS, xUnit HTTP integration tests, Docker files, CI | React/Next.js, PostgreSQL, CQRS framework, distributed infrastructure |
| Hosting | Local run, publish artifact, local Docker configuration | Public deployment, cloud resources, infrastructure as code |

## Why these choices

- **SQLite:** no database server installation. It still gives relational constraints and atomic transactions.
- **Plain JavaScript:** one application to start, no second dev server or Node toolchain. A React/TypeScript frontend can be a future independent step.
- **One commerce service:** the important rules have an obvious home without enterprise layering.
- **No Redis/RabbitMQ:** this edition has no asynchronous delivery requirement or expensive distributed cache to justify them.
- **Real integration tests:** focus on money, stock, permissions, and data isolation instead of tests that only repeat getters and setters.

## Reasonable next learning steps

1. Split the growing route file into feature endpoint classes.
2. Add EF migrations and PostgreSQL, including concurrency tests against PostgreSQL.
3. Add product/customer editing improvements and richer reports.
4. Add emailed invitations and account recovery with expiring single-use tokens.
5. Add Playwright browser regression tests and a typed frontend if desired.
6. Add genuine background work only when implementing email or scheduled exports.

There are no placeholder microservices, fake cloud integrations, or claims that simulated payments are real payments.
