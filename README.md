# memmesh-dotnet

Official **.NET** SDK for **[MemMesh](https://memmesh.ai)** — memory + prediction
for AI agents. Semantic recall, a bi-temporal knowledge graph, belief revision,
reflection, and calibrated forecasting.

```bash
dotnet add package MemMesh
```

## Quickstart

```csharp
using MemMesh;

var mm = new MemMeshClient("sk-...", "proj_...");

// Remember something
await mm.Memory.ObserveAsync("Prefers email over phone.",
    subject: new Subject("contact", "sarah"));

// Recall it, semantically
var hits = await mm.Memory.SearchAsync("how to reach sarah", limit: 5);
foreach (var h in hits) Console.WriteLine(h.Content);

// Synthesize higher-order insights, with provenance
var res = await mm.Memory.ReflectAsync(maxInsights: 3);
foreach (var i in res.Insights) Console.WriteLine($"{i.Content} ({i.Confidence:P0})");

// Point-in-time knowledge graph
var edges = await mm.Context.QueryGraphAsync(asOf: "2026-03-01T00:00:00Z");
```

## Surface

`mm.Memory` (observe/create/search/list/update/delete/stats/confirm/promote/feedback,
**Reflect**, **PrefetchRelated**, Dedup) · `mm.Lattice` (Predict/Mine/Profile/
PredictByCohort/Calibration) · `mm.Context` (Build/**BatchBuild**/**QueryGraph**) ·
`mm.Events` · `mm.Alerts` · `mm.Learning` · `mm.Typed` · `mm.Compliance` · `mm.Health`.

Config: `new MemMeshClient(apiKey, projectId, baseUrl?, httpClient?)`.
API errors throw `MemMeshException` (`.Status`, `.Body`).

Apache-2.0 · [memmesh.ai](https://memmesh.ai) · [docs](https://docs.memmesh.ai)
