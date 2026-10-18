# Verification record

Checked on 18 October 2026, using .NET SDK 10.0.400 on Windows.

- Release integration suite: **9 passed, 0 failed**.
- Release publish: completed to `artifacts/app`.
- JavaScript syntax: `node --check src/Clustr/wwwroot/app.js` passed. Node is only used for this optional development check, not to run Clustr.
- Browser walkthrough: registered a local test account, created a workspace, loaded six café products, completed a €3.80 sale, inspected its receipt, refunded it with restock, and verified the overview showed €0 net sales and €3.80 refunded.
- Browser console: no warnings/errors observed in the sample-catalog check.
- Screenshot: `docs/screenshots/overview.jpg` records the real browser dashboard after that refund.

The browser walkthrough used a separate `artifacts/browser-data` database. It does not create a default account in the normal `src/Clustr/App_Data` database.

Docker is not installed in the verification environment, so the Docker build and Compose startup have **not** been executed. The GitHub Actions workflow has been written but has **not** run on GitHub. Mobile breakpoints are implemented in CSS but have not had a separate device-browser test. There is no public deployment.

To repeat the main automated check:

```powershell
dotnet test tests/Clustr.Tests/Clustr.Tests.csproj --configuration Release
```
