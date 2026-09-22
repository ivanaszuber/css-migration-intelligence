# CSS Migration Intelligence

An open-source, synthetic proof of concept for exploring how a portfolio of legacy client CSS could become a shared UI-kit and governed, per-module configuration. It is a learning and discussion tool, not a production migration service.

The demo loads five **fictional clients**. It groups CSS by Home & communications, Learning, Operations, and shared app styling; proposes configurable properties using deterministic rules; lets a human approve a contract; and reports transformed versus unchanged declarations by client and module. Unchanged CSS can be given a draft review action, but the source files are never modified. A local preview demonstrates editable font and image controls.

No LLM is required for the core workflow. An optional Azure OpenAI recommendation provider exists in the API but is disabled unless an endpoint and deployment are configured. The visible migration percentages measure CSS declarations, not visual fidelity or production readiness.

## Run locally

Requirements: .NET 8 SDK and Node.js compatible with Angular 16.

In terminal 1, from the repository root:

```bash
Portfolio__JsonPath="$PWD/samples/portfolio.manifest.json" \
  dotnet run --project src/CssMigration.Intelligence.Api --urls http://localhost:5250
```

In terminal 2:

```bash
cd web
npm ci
npm start
```

Open <http://localhost:4500>, then choose **Run demo ZIP**. The client talks to the local API at port 5250. You can also run `dotnet test CssMigration.Intelligence.sln` and `npm run build` from `web/`.

## Workflow

1. Run the demo ZIP. The same in-memory importer accepts an uploaded ZIP of UTF-8 CSS arranged as `client-name/file.css` or `client-name.css`.
2. Review property candidates by module and by client. Different values *between* clients are normal. A value conflict means multiple measured values *within one client* for one proposed control.
3. Approve safe properties and generate the migration report. Click a client outcome to filter unchanged CSS to that client.
4. Record a local draft action for unchanged CSS: align to a supported value, retain a governed exception, or propose a new UI-kit/configuration control. Download the draft for review. Nothing is applied to source CSS.
5. Preview editable module controls. Image selection is a local browser preview only; neither image bytes nor configuration are uploaded or published.

The generated shared CSS and per-client migration artifacts are **illustrative**. They do not establish that a real UI-kit can replace every legacy rule. A production implementation needs real repository and schema discovery, privacy approval, visual regression tests, accessibility review, security controls, module-owner decisions, and customer sign-off.

## Safety boundary

This repository contains synthetic stylesheets and fictional client names only. Do not upload actual customer CSS, credentials, personal data, or proprietary architecture to a public instance. The sample API has no authentication, persistence, or production tenancy controls. Deploy it only with an appropriate access boundary and approved data-handling plan.

The Angular 16 dependency set is intentionally an older migration-lab baseline. A fresh `npm ci` currently reports dependency audit findings; upgrade and security-review dependencies before company or production use.

## Structure

- `src/CssMigration.Intelligence.Core/`: CSS inventory, candidate coverage, and deterministic rules.
- `src/CssMigration.Intelligence.Service/`: ZIP intake, portfolio analysis, migration compilation, and module reporting.
- `src/CssMigration.Intelligence.Api/`: ASP.NET Core endpoints and optional Azure AI advisory adapter.
- `web/`: Angular 16 desktop workbench.
- `samples/`: five synthetic CSS portfolios and manifest.
- `tests/`: .NET tests.

The code is licensed under [MIT](LICENSE). It is intended as a reference for a future company-managed implementation, not as company source code or a representation of any company's production system.
