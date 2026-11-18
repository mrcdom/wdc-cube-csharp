# Cube tutorial (C#)

Port of `wdc-cube/apps/cube-tutorial`: a page frame (menu, alerts), a session-free restricted area, a subscriptions
list with a detail dialog, and TodoMVC (with a 1000-item stress mode and a clock).

| Project | What it is |
|---|---|
| `Tutorial.Presentation` | Places, keys, presenters, scopes, service. No view technology. |
| `Tutorial.Presentation.Tests` | The presentation tested without any UI (ported from `presentation-test`). |
| `Tutorial.Blazor` | The views, in Blazor (interactive server, no prerender). |
| `Tutorial.Blazor.Tests` | The whole page on Blazor's renderer (bUnit): clicks, screen and address. |

## Run

```bash
cd samples/Tutorial/Tutorial.Blazor
dotnet run            # uses the launch profile: Development, http://localhost:5180
```

Run it in **Development** (the launch profile does it). Outside a published build, `_framework/blazor.web.js` is only
served in Development; started as Production from the project folder, the page loads but never becomes interactive.

Try: open the to-dos, filter, reload (the filter comes back from the address), open a subscription, press Back.

## Differences from the TypeScript tutorial
- Rows of the to-do list are reconciled by identity (the TS tutorial rebuilt them on each load).
- The clock uses the application's `TimeProvider`, so tests control time.
- Positive user ids generate a list instead of calling a public API.
- The address carries the full place path (`subscriptions/detail?sd-site=2`).
