# Feature-flag platforms with a UI and real experimentation

Research note (2026-08-10). Primary sources only — each project's `LICENSE` file read from
`raw.githubusercontent`, full source trees cloned and scanned locally, official docker-compose
files, official docs, the nuget.org v3 API, `cncf/landscape` `landscape.yml`, and authenticated
`gh api`. No comparison articles, no blog posts.

**The question.** [ADR-0014](../adr/0014-feature-flags-openfeature.md) chose flagd, knowingly
accepting that it has no UI. This note asks what it would take to get **a management UI and real
A/B experimentation** instead, under a licence usable in this repo without hurdles, self-hostable
by a reader who clones and runs `docker compose up`.

**The answer, up front: nothing clears all four bars at once.** Every actively-maintained candidate
either has no statistical engine, or has one that cannot be demonstrated without infrastructure the
reader must build. The two OSS projects that ever shipped the full loop are both dead.

---

## 1. What "real experimentation" means here, and why it is the binding constraint

The bar applied throughout is four legs, all of which must be in the OSS build and usable without a
licence key:

1. **variant assignment** — nearly every candidate has this
2. **goal / metric collection**
3. **statistical analysis**
4. **a results readout** showing lift and significance or an equivalent

Leg 1 alone is percentage rollout, which flagd already does via `fractional`. Most products in this
category market themselves as experimentation platforms while shipping only leg 1.

**The single most efficient discriminator** was a recursive tree scan per repo for
`bayes|signific|statistic|p-value|confidence`. It separates real engines from marketing in one call.
Treat its output with care: on JS/TS codebases `pvalue` matches `mapValues` and `t-test` matches any
identifier ending `-test`, so hits must be read, not counted.

---

## 2. The verdict table

Licence is reported for the **server** and the **.NET client** separately, because a server run as
its own container never links against the consuming code. Only the client SDK's licence affects this
repo.

| Candidate | Server licence | .NET client | UI in OSS | Real experimentation | One-command self-host | .NET OpenFeature provider |
|---|---|---|---|---|---|---|
| **GrowthBook** | MIT (+ enterprise carve-out) | MIT | yes, gated | **YES — ungated** | flags yes / experiments **no** | 0.0.1, 16 mo stale, no tracking |
| **Bucketeer** | Apache-2.0, no open-core split | — | yes, ungated | **YES — ungated** | **no** — 12 containers | **none exists** |
| **Unleash** | **AGPL-3.0-or-later** | Apache-2.0 | yes, capped | no | **yes** — 2 containers | **official, `net10.0`** |
| **Flipt** | FCL-1.0-MIT (non-OSI) | MIT | yes, ungated | no | yes | community + native OFREP |
| **flagr** | Apache-2.0 | none declared | yes, ungated | no (says so itself) | **best** — 1 container | none (community, dead) |
| **Flagsmith** | BSD-3 (+ private wheel) | — | yes, heavily gated | Enterprise beta, **unshipped** | 4 containers | native provider |
| **FeatureHub** | Apache-2.0 **+ Commons Clause** | MIT | yes, ungated | no | yes | **none — no OpenFeature at all** |
| **GO Feature Flag** | MIT | — | **no** | no | yes | current, native OFREP |
| **Featurevisor** | MIT | — | no, by design | no | n/a — no server | **none exists** |

Swept and dismissed: Statsig, Eppo, Amplitude Experiment, Hackle, Tggl (stats behind a SaaS
boundary); Abby (AGPL, no stats, 17 months stale); OpenReplay (session replay; its flag UI has been
removed from the tree); PlanOut (archived by Facebook); Unlaunch and Molasses (**dead** —
`molasses.app` states "permanently shut down as of October 2025"; `unlaunch.io` 301s to an unrelated
business).

---

## 3. The two that have real statistics

### GrowthBook — the engine is real, the demo is not turnkey

`gbstats` lives at `packages/stats/`, carries **its own MIT licence**, is built into the OSS Docker
image, and is spawned in-process by the backend. It implements Bayesian (`EffectBayesianABTest`,
`GaussianPrior`), frequentist (`TwoSidedTTest`), sequential, CUPED and post-stratification.

**Computing results is not a commercial feature** — verified against
`packages/shared/src/enterprise/license-consts.ts`, where the OSS tier is `new Set([])` (zero of 72
commercial features) yet the results components contain no premium gates. Lift, credible interval and
chance-to-win render with no licence key. OSS loses CUPED, sequential testing, post-stratification and
bandits — as **silent downgrades**, not blocks; the analysis still runs.

**Why it still fails the governing constraint:** GrowthBook stores no events. It queries a warehouse,
and the official compose ships none. The zero-setup Managed Warehouse is hard-gated
(`if (!IS_CLOUD) return 403`); the Event Forwarder needs Pro. A reader gets working feature flags and
an experiment results screen with **nothing behind it**. Producing one number means adding a
warehouse, wiring the SDK's tracking callback to write exposure rows, emitting metric events, then
modelling fact tables in the UI.

That is not a fixable flaw — **it is the same architectural choice that makes the statistics
possible.** GrowthBook can analyse properly *because* it delegates storage to a warehouse it does
not own.

**The .NET provider is the sharpest problem for this repo.** `GrowthBook.OpenFeature` is version
**0.0.1**, published once on 2025-04-22 and untouched since, pinning OpenFeature 2.4.0. It implements
only the five `Resolve*Async` methods — there is **no tracking callback**, so the OpenFeature path
emits no exposure events, which is exactly what the stats engine consumes. The workaround is a second
constructor taking a pre-built native SDK, which means dropping out of the abstraction to do
experiments.

### Bucketeer — the most complete engine, and unusable here

Apache-2.0 throughout with **no open-core split at all**: `pkg/experimentcalculator/` ships
`statistics.go` (gonum), `normal_inverse_gamma.go`, `sequential_bayes_factor.go` and `srm.go`
(sample-ratio-mismatch), plus a dedicated **HttpStan** (Stan MCMC) container. The results contract
carries `cvr_prob_best`, `cvr_prob_beat_baseline`, `expected_loss`; the readout screens exist.

Two independent disqualifiers, either fatal:

- **Self-host is 12 services** (`mysql`, `postgres`/TimescaleDB, `redis`, `migration`, `web`, `api`,
  `batch`, `subscriber`, `httpstan`, `batch-cron`, `prometheus`, `nginx`) on ports including 80 and
  443, requiring TLS certs, an **admin-privileged hosts-file edit**, `make` targets rather than
  `docker compose up`, and manual account plus API-key bootstrap.
- **No .NET SDK exists.** nuget.org returns 404; the org has no C# repo and no .NET OpenFeature
  provider.

**Not CNCF-affiliated**, contrary to a common assumption. It appears in `cncf/landscape` with no
`project:` field — the same as Unleash, Flagsmith and Split. Compare OpenFeature in that file, which
carries `lfx_slug`, `dev_stats_url` and `clomonitor_name`. Landscape inclusion is a directory listing.

---

## 4. Findings that correct widely-held beliefs

- **Unleash is AGPL-3.0-or-later, not Apache-2.0.** It relicensed on **2026-05-26**, commit
  `34b5661e`, PR #12086 "task: Move to AGPL-v3". Every comparison predating that is stale. Its .NET
  SDK is a separate Apache-2.0 repo, so linking is unaffected.
- **Unleash OSS is hard-capped at one project and three fixed environments**, enforced server-side in
  the data-access layer (`project-store.ts:139`, `environment-store.ts:171`) via a flag a self-hoster
  cannot override in production. *Inference: for a single reference solution with three flags, one
  project and `default`/`development`/`production` is plausibly the correct shape anyway — this cap
  is less damaging here than for a real multi-team deployment.*
- **Flipt v2 relicensed to FCL-1.0-MIT**, source-available and non-OSI, with an anti-compete clause
  and an explicit anti-circumvention term. It converts to MIT after two years. Its `main` branch
  (v1) remains GPL-3.0 — pinning `@main` gets a materially different licence from the default branch.
- **FeatureHub misstates its own licence.** `readme.adoc` says "Apache 2.0"; `LICENSE.txt` has carried
  the **Commons Clause** since its first commit in 2020.
- **Flagsmith's `MAX_PROJECTS_IN_FREE_PLAN = 1`** is a hardcoded constant, not an env var. Its audit
  log is paywalled in the dashboard despite the docs page carrying no gating notice. Its native
  experimentation is Enterprise beta gated behind a Flagsmith-hosted remote flag defaulting to
  `false`, and its own status table marks the results scorecard **"Planned"** — not shipped even to
  Enterprise.
- **flagr states its own limitation honestly**, which is rare here:
  `docs/flagr_eval_exposure_pipeline.md:5` — "Flagr doesn't pick your streaming backend, and it
  doesn't run significance tests." Its GitHub description markets it as A/B testing; the docs win.
- **GO Feature Flag has no UI.** The `studio` repo is a 39-byte stub reading "A WIP repository to work
  on an UI"; `flag-management` is unreleased, dormant since 2025-05-16, and carries **no LICENSE
  file**.

---

## 5. Telemetry defaults

Three of the candidates phone home out of the box. Worth setting explicitly in any compose block.

| Product | Variable | Default | Documented? |
|---|---|---|---|
| Flagsmith | `ENABLE_TELEMETRY` | on | **no** — appears nowhere in the docs |
| GrowthBook | `DISABLE_TELEMETRY` | on (i.e. sends) | yes, with full disclosure of the payload |
| Unleash | `CHECK_VERSION`, `SEND_TELEMETRY` | both on | partially |
| Flipt | `FLIPT_META_TELEMETRY_ENABLED`, `FLIPT_META_CHECK_FOR_UPDATES` | both on | telemetry yes, update-check separately |
| flagr | — | none found | n/a |

---

## 6. Recommendation

**Keep flagd unless the UI is worth a migration on its own.** No candidate delivers experimentation
that a reader can actually see without building infrastructure, so switching does not buy the thing
that motivated the question.

If the **UI** is the goal, **Unleash** is the strongest fit despite having no statistics: two
containers, one port, no signup or licence key, and by a wide margin the best .NET story in the field
— an **official** `OpenFeature.Providers.Unleash` 0.1.1 targeting `net10.0` and pinned to
`OpenFeature [2.0.0, 3.0.0)`, over a client SDK with 23M downloads. Note it is pre-1.0 and two months
old, so its API may churn.

If **experimentation** must be in-product, **GrowthBook** is the only viable option, and it should be
shipped in two tiers: the default stack teaches OpenFeature-driven flagging against a real console,
and experiment analysis sits behind an opt-in compose profile with a seeded Postgres. That preserves
"clone and run" while still showing real statistics — but it is repo work to write and maintain, not
something GrowthBook provides.

**A third path, and arguably the best pedagogy for this repo:** this stack already runs Prometheus,
Grafana, Jaeger and an OTel collector. Assign a variant with flagd, emit conversion events through
OpenFeature's Tracking API, and read lift from a Grafana panel. That teaches the mechanics rather
than hiding them behind a vendor console, adds zero containers, and keeps the Apache-2.0 position.
It does not give a significance calculation — which, notably, Flagsmith Enterprise does not ship
either.

---

## 7. Negative findings and limits of this note

- **No candidate was executed.** Every claim is read from source, licence files, docs and Dockerfiles.
  Neither the GrowthBook compose stack nor the `GrowthBook.OpenFeature` 0.0.1 package was run against
  OpenFeature 2.14.0 — that resolution rests on NuGet floor-version semantics, which does not prove
  the compiled surface is unchanged.
- **Absence claims rest on full-tree scans, not code search.** GitHub code search is quota-capped and
  returns partial results; every "X does not exist" here comes from a materialised clone or a full
  recursive tree.
- **`api.github.com`'s `license` object is unreliable** on exactly the repos that matter — Flipt,
  OpenReplay, FeatureHub and PlanOut all report `NOASSERTION`/`Other`. Every licence conclusion above
  comes from the raw file text.
- **`archived: false` is a weak liveness signal.** Wasabi, Sixpack and Abby are all unarchived and
  effectively dead. Latest *commit* date plus a product-domain reachability probe is what caught
  Unlaunch and Molasses.
- **Labelled inference, not vendor guidance:** the judgement that a container's server licence does
  not bind this repo; that Unleash's project/environment cap is tolerable for a single reference
  solution; the two-tier GrowthBook compose shape; and the Grafana-based third path in §6.

## Related

- [`feature-flag-providers.md`](feature-flag-providers.md) — the original provider comparison.
- [`flagd-implementation.md`](flagd-implementation.md) — how flagd would land here, and why it has no UI.
- [ADR-0014](../adr/0014-feature-flags-openfeature.md) — the accepted decision this note reopens.
