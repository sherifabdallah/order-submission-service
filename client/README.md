# Order Desk client

Angular 22 client for the order-submission service. See the [root README](../README.md) for the
whole application and [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md#the-client) for the client design.

```bash
npm ci
npm start                # dev server on http://localhost:4200, proxies /api to http://localhost:5080
npm test                 # Vitest unit tests
npm run build            # production build into dist/
npm run build:embedded   # production build into ../src/OrderSubmission.Api/wwwroot (single deployable)
```

`package.json` pins Vitest's Vite and Rolldown to the versions `@angular/build` uses, so the builder
and the test runner share one native Rolldown binary.
