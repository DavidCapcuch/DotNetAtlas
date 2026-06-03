# Checkout Saga Stuck — Ops Runbook

> When the Checkout saga reaches terminal state `CompensationStuck`, this runbook guides the on-call engineer from alert to resolution.
>
> Related: [checkout-saga.md](checkout-saga.md), [ADR-0004](../adr/0004-checkout-saga-topology.md), [error-taxonomy.md](error-taxonomy.md), [events-catalog.md](events-catalog.md), [ADR-0029](../adr/0029-order-keyed-saga-and-pre-assigned-orderid.md), [ADR-0030](../adr/0030-retire-dedicated-correlationid.md).
>
> Triage keys on **`order_id`** — the durable business key after the saga was re-keyed on OrderId (ADR-0029) and the dedicated correlation id was retired (ADR-0030).

---

## 1. When this fires

The saga enters `CompensationStuck` when compensation takes longer than `CompensationTimeout` (default **300 seconds**) without completing. This means one of:

- **Payments refund call failed repeatedly** and the refund was never processed (reservation may have been released but money is still held).
- **Inventory release calls failed** for some reservations (stock is still reserved but payment was refunded).
- **Kafka partition unavailable** on a command topic (consumer cannot read the release command).
- **Saga service crashed mid-compensation** (unlikely — persisted state resumes on restart).
- **Down-stream DLT saturation** (release/refund messages landed in DLT but no consumer is draining them).

A saga in `CompensationStuck` is **terminal from the orchestrator's perspective** — it will not self-recover. Manual intervention is mandatory to avoid orphaned reservations, double-refunds, or held funds.

### Blast radius

| State at time of stuck | Potential user impact | Money impact |
|------------------------|----------------------|--------------|
| Refund pending, stock released | Buyer charged, order failed | Money held with gateway until refund replays |
| Refund completed, stock still reserved | Buyer refunded, order failed | Stock unavailable to other buyers until `ReservationExpiryWorker` releases it (TTL window) |
| Both release and refund pending | Buyer charged, stock held, order failed | Worst case — both sides need replay |

---

## 2. Alert configuration

### 2.1 Grafana alert rule

```yaml
# Alert rule (YAML sketch — actual rule lives in Grafana provisioning)
- alert: CheckoutSagaStuck
  expr: increase(saga_checkout_stuck_total[5m]) > 0
  for: 5m
  labels:
    severity: page
  annotations:
    summary: "Checkout saga stuck — manual intervention required"
    description: "{{ $value }} checkout saga(s) reached CompensationStuck in the last 5 minutes."
    runbook: "https://internal.docs/saga-stuck-runbook"
```

### 2.2 Sink

**PagerDuty service:** `checkout-saga` → on-call engineer (primary), team channel `#ops-checkout` (secondary).

### 2.3 Related alerts (should be paging already if any are a factor)

| Alert | Threshold | Meaning if firing |
|-------|----------|-------------------|
| `PaymentsRefundFailureRate` | `> 1%` for 5m | Payment gateway likely down or misbehaving |
| `InventoryReleaseFailureRate` | `> 1%` for 5m | Inventory consumer bug, DB issue, or topic issue |
| `KafkaConsumerLag{consumer_group="saga-checkout"}` | `> 1000` for 10m | Saga worker falling behind or crashed |
| `KafkaDLTMessages{topic=~".*\\.DLT"}` | `> 10` for 5m | Messages rejected repeatedly — check DLT |
| `SagaCheckoutInProgress` (gauge) | `> 500` sustained | General saga throughput issue, not necessarily stuck |

If any of the above are already paging alongside `CheckoutSagaStuck`, focus root-cause investigation there first — they are likely the cause, not the symptom.

---

## 3. Investigation checklist

### Step 1 — Identify the stuck saga instances

```sql
SELECT order_id,
       user_id,
       error_code,
       error_message,
       compensation_started_at_utc,
       last_modified_utc
FROM saga.checkout_saga_state
WHERE current_state = 'CompensationStuck'
ORDER BY compensation_started_at_utc DESC
LIMIT 100;
```

Record the `order_id` values — the durable business key (ADR-0029/ADR-0030); every subsequent step hangs off these ids.

### Step 2 — Correlate across services

Correlate across services on the `order_id` — the durable business key (ADR-0030). For
distributed traces, pull the `traceId` from the saga's logs/spans in Jaeger / Tempo; note it is a
separate, **sampled** and short-retention key (ADR-0030), so it may be absent for older incidents.
Look for:

- Was **payment captured**? Check `payments.payment_transactions`:
  ```sql
  SELECT id, order_id, status, captured_at_utc, refunded_at_utc
  FROM payments.payment_transactions
  WHERE order_id = '{order_id}';
  ```
- Was **any reservation confirmed** before the saga got stuck?
  ```sql
  SELECT reservation_id, order_id, status, release_reason
  FROM inventory.reservation_audit
  WHERE order_id = '{order_id}';
  ```
- Are **release commands in DLT**?
  ```bash
  docker compose exec kafka kafka-console-consumer \
    --bootstrap-server localhost:9092 \
    --topic inventory.reservation-commands.Inventory.DLT \
    --from-beginning \
    --max-messages 50 \
    --property print.key=true | grep '{order_id}'
  ```
- Are **refund commands in DLT**? Same command against `payments.payment-commands.Payments.DLT`.

### Step 3 — Classify the root cause

| Symptom | Root cause | Go to |
|---------|-----------|-------|
| Refund command in `payments.payment-commands.Payments.DLT` | Payment gateway outage or gateway-client bug | § 4.1 |
| Release command in `inventory.reservation-commands.Inventory.DLT` | Inventory consumer bug or Inventory DB outage | § 4.2 |
| `ReservationExpiryWorker` already expired reservations during compensation | TTL raced with refund — reservations are already `Released` with `ReleaseReason='Expiry'` | § 4.3 |
| Kafka consumer lag on `saga-checkout` | Saga worker OOM, crash-looping, or restarting | § 4.4 |
| No DLT, no gateway errors, saga state looks consistent but no compensation events | State corruption (rare) | § 4.5 |

### Step 4 — Quick health snapshot

Before acting, capture the system snapshot in the incident channel:

```bash
# Saga pods
kubectl get pods -l app=saga-orchestrators -o wide
# Inventory consumer
kubectl get pods -l app=inventory-api -o wide
# Payments consumer
kubectl get pods -l app=payments-api -o wide
# Kafka consumer groups
docker compose exec kafka kafka-consumer-groups \
  --bootstrap-server localhost:9092 \
  --describe --group saga-checkout
```

Paste output into the incident thread — post-mortem reviewers will need it.

---

## 4. Recovery procedures

> **Gate before every recovery action:** the root cause from § 3 must be classified. Running § 4.5 on a healthy-but-delayed saga will orphan state. If you are not sure, **do not touch state** — page a second engineer.

### 4.1 Payment gateway outage

Context: refund commands are piling up in `payments.payment-commands.Payments.DLT`; gateway returns 5xx or timeouts.

1. Confirm gateway has recovered — synthetic test against Payments health probe.
2. Replay refund commands from DLT:
   ```bash
   # Internal tool — replays DLT messages to the live topic in batches
   ops-replay --source payments.payment-commands.Payments.DLT --dest payments.payment-commands \
              --order-ids order-ids.txt --batch 50
   ```
3. Watch `payments.payment_transactions.status` flip from `Captured` → `Refunded` for the affected orders.
4. Saga will NOT auto-transition out of `CompensationStuck` after manual replay — follow § 4.5 to mark `Compensated` once all side-effects are verified clean.

### 4.2 Inventory consumer bug

Context: release commands in `inventory.reservation-commands.Inventory.DLT`; Inventory logs show handler exceptions.

1. Capture the stack trace from Inventory logs — root cause might be a recently-shipped bug; if so, start a rollback in parallel.
2. Deploy the fix (or roll back the bad commit).
3. Replay release commands from DLT:
   ```bash
   ops-replay --source inventory.reservation-commands.Inventory.DLT --dest inventory.reservation-commands \
              --order-ids order-ids.txt --batch 50
   ```
4. Verify each affected reservation row in `inventory.reservation_audit` has `Status='Released'` and `ReleaseReason='SagaCompensation'` (not `Expiry` — see § 4.3 if so).
5. Apply § 4.5 to mark saga `Compensated`.

### 4.3 Reservation already expired mid-compensation (common race)

Context: `ReservationExpiryWorker` runs on a TTL (15 min default). If compensation is slow, reservations may self-release via TTL before the saga's `ReleaseReservation` command lands.

Detection:

```sql
SELECT reservation_id, status, release_reason
FROM inventory.reservation_audit
WHERE order_id = '{order_id}';
-- If every row has status='Released' AND release_reason='Expiry', the stock side is already resolved.
```

Action: if **all reservations for the OrderId already have `Status='Released'`** (regardless of reason), the inventory side is compensated. Skip DLT replay for release commands. Only handle the refund side (§ 4.1). Then apply § 4.5.

**This is the most common `CompensationStuck` scenario** in healthy production — a transient Payments slowdown lets the reservation TTL win the race. It is benign and does not need a post-mortem unless the frequency climbs.

### 4.4 Saga worker OOM / restart

Context: `kubectl get pods -l app=saga-orchestrators` shows `CrashLoopBackOff` or restarts > 0 in the last hour.

1. Capture pod logs for the crash loop:
   ```bash
   kubectl logs -l app=saga-orchestrators --previous --tail 500
   ```
2. MassTransit **resumes saga state on restart** from the saga state store — no action is usually needed after the pod is healthy again.
3. If the saga is still `CompensationStuck` after the pod stabilizes for > `CompensationTimeout`, the in-flight compensation command was lost. Manually republish the relevant command:
   ```bash
   ops-saga-kick --order-id {order_id} --action compensate
   ```
4. If repeated OOMs — scale vertically (bump memory limit) and file a performance bug. Do NOT just increase `CompensationTimeout`; that masks the problem.

### 4.5 Manual state recovery (last resort)

**Gate:** only perform after confirming via § 3 investigation that:

- Refund is completed (check `payments.payment_transactions.status='Refunded'`), OR was never captured.
- All reservations are released (check `inventory.reservation_audit.status='Released'` for every row).
- No commands for this order id remain in any DLT.

Then, and only then:

```sql
BEGIN;
UPDATE saga.checkout_saga_state
SET current_state = 'Compensated',
    compensation_completed_at_utc = now(),
    last_modified_utc = now()
WHERE order_id = '{order_id}'
  AND current_state = 'CompensationStuck';
-- Verify 1 row affected before COMMIT.
COMMIT;
```

Audit-log the manual update in the incident channel including a link to the § 3 verification snapshot. Incorrect state update results in orphaned stock reservations or double-refunds.

### 4.6 What NEVER to do

- **Never** delete a row from `saga.checkout_saga_state` to "reset" it. The order exists and cannot be un-created.
- **Never** replay `CheckoutStartedEvent` to "restart" the saga for the same order id. Commands are not idempotent across saga runs.
- **Never** adjust `CompensationTimeout` live to move a saga out of `CompensationStuck`. The state is assigned on entry; changing the setting does nothing for in-flight instances.

---

## 5. Post-mortem template

Every `CompensationStuck` incident requires a post-mortem unless it was a § 4.3 benign TTL race AND the overall frequency is within the monthly baseline.

```markdown
# Checkout Saga Stuck Incident — YYYY-MM-DD

## Summary
One-paragraph plain-English summary.

## Timeline (all times UTC)
- T+0 — alert `CheckoutSagaStuck` fired (N saga instances)
- T+N — on-call acked
- T+N — root cause identified as <category>
- T+N — recovery action started (§ 4.X)
- T+N — all affected sagas marked `Compensated`
- T+N — alert cleared

## Impact
- N saga instances stuck
- N users affected
- N refunds delayed, $X held
- N reservations held beyond normal TTL
- User-facing symptom: <e.g., "order stuck in failed state in order history for X minutes">

## Root cause
<Narrative — what was the proximate + underlying cause>

## Detection
<How did we find out? Did alerting lead detection, or did a user report first?>

## Recovery
<Which § 4.X procedure(s) were used. Any deviations from runbook.>

## What went well

## What went poorly

## Action items (5 Whys)
- [ ] AI-1 ...
- [ ] AI-2 ...
- [ ] AI-3 ...
```

Store the filled post-mortem under `docs/postmortems/YYYY-MM-DD-checkout-saga-stuck.md`.

---

## 6. Prevention checklist

Run this checklist quarterly; items that fail become tickets.

- [ ] CI tests cover all 11 saga states + every compensation path (see [checkout-saga.md](checkout-saga.md) state machine).
- [ ] Chaos testing: inject Payments/Inventory outage mid-checkout during a monthly load test; confirm saga enters `CompensationStuck` only when expected and recovers cleanly.
- [ ] Kafka consumer lag on `saga-checkout`, `inventory-reservations`, `payments-payments` is monitored continuously with paging thresholds.
- [ ] `saga_checkout_stuck_total` counter dashboard widget is **always visible** on the eShop main dashboard.
- [ ] DLT depth alerts exist for `inventory.reservation-commands.Inventory.DLT` and `payments.payment-commands.Payments.DLT` with 10-minute thresholds.
- [ ] `CompensationTimeout` default (300s) is reviewed against observed p99 compensation duration — if p99 > 150s, revisit; consider lowering timeout or fixing tail-latency.
- [ ] `ReservationExpiryWorker` TTL (15 min) is comfortably longer than `CompensationTimeout` × 2 — otherwise § 4.3 races become the norm.
- [ ] Runbook (this document) is linked from the PagerDuty incident body via the alert's `runbook` annotation.
- [ ] `ops-replay` and `ops-saga-kick` tools are installed in every on-call engineer's runbook toolkit and tested during onboarding.
- [ ] New on-call engineers shadow a real or simulated `CheckoutSagaStuck` drill before being placed in the rotation.
