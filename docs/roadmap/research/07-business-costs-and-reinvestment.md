# Free access subscriptions and development funding

Planning proposal, 30 September 2026. The founder is one person working with coding agents. Early hosting and incremental AI spending must stay below $100 per month. The founder intends to put revenue back into development. The prices, quotas, conversion assumptions and revenue examples below are proposals to test, not offers, forecasts or evidence of demand.

## Start with a service people return to

The paid product should grow from a useful free experience: explore a shared Earth, make something understandable with a friend, save it, and return to find it intact. Charging for coordinates before this loop works would test speculative interest rather than whether the game is valuable.

Keep free exploration, manual creation, shared community activity, one bounded saved sandbox and invitations to friends. Charge later for demonstrated services: additional saved projects, additional backups, scheduled hosted sessions, larger measured storage, and a dependable managed address or portal. Keep shared physics rules and permissions equal. Paying must not bypass effect, consent, safety or server-work limits.

Copyable software supports optional self-hosting and export over time. Customers would pay for operation, community and convenience. Kitely documents a service around OpenSimulator, downloadable world archives and backups; this is evidence that the pattern exists, not evidence of our profitability. Second Life distinguishes mainland presence from private-region control and publishes recurring region maintenance prices. Neither reference determines an appropriate Enfractal price. [Kitely](https://www.kitely.com/), [Second Life private regions](https://go.secondlife.com/land/private-pricing).

## Operating envelope before revenue

Use $95 as an internal maximum planning envelope so the user-requested total remains below $100. These are allocation ceilings, not vendor invoices. No service is ordered by this plan.

| Category | Monthly allocation | Control |
|---|---:|---|
| One regional Linux host | $55 | Fixed-size, no automatic scaling; verify 8 GB-class RAM and benchmark CPU headroom |
| Off-host backups and map/object storage | $6 | Bounded uploads, retention and request counts; no free-tier assumption required |
| Incremental AI usage | $15 | Up to $10 in-game creation; at most $5 additional development API experimentation; one combined ledger |
| Domain amortization | $2 | Optional until externally reachable alpha; first purchase is an annual cash expense |
| Monitoring, logs and incidental tooling | $2 | Start with local/free tools, bounded log retention |
| Unspent reserve for tax, IPv4, FX, usage variance and contingencies | $15 | Do not treat this as permission for background AI jobs |
| **Total ceiling** | **$95** | **Close admissions or reduce optional work before raising the limit** |

The first five lines total $80. The $15 reserve protects against incomplete quotes and usage uncertainty. If the actual all-in host price cannot fit, use scheduled private sessions on a cheaper measured host; do not silently move to an expensive managed stack. Backups remain required even when the game runs only on scheduled evenings. Sleeping worlds save CPU capacity, not the fixed VPS bill: Hetzner bills an allocated server even while powered off, until it is deleted. This plan does not depend on repeatedly deleting the Home server. [Billing policy](https://docs.hetzner.com/cloud/billing/faq/).

Count an annual domain purchase or renewal against that month's actual cash ledger. Amortization is for cost comparison only; defer the domain or reduce optional AI that month if necessary to stay below $100.

This envelope excludes founder wages, the computer/electricity already in use and any pre-existing subscriptions. It includes new metered game/development AI charges. Existing coding-agent subscription cost is unknown; if the user's $100 is meant to include those existing bills, subtract them first and reduce this plan's available balance. New API calls are not assumed to be covered by a consumer subscription. Public distribution, specialist reviews or business setup may create one-time costs and are separate launch gates, not secretly affordable inside $95.

Current price research is an anchor, not a provider commitment. Hetzner's official table lists US CCX13 at $50.99/month excluding IPv4 and tax; the CCX13 specification lists two dedicated vCPUs, 8 GB RAM and 80 GB storage. Verify the regional order configuration and included transfer before purchase. The cheaper US CPX21 is only 4 GB and cannot stand in for this host envelope. Two vCPUs still need the combined-world load test; RAM is not a capacity proof. Cost-optimized offers can be unavailable. Select a host near the initial testers and measure actual tick jitter. [Official prices](https://docs.hetzner.com/general/infrastructure-and-availability/price-adjustment/), [CCX13 specification](https://www.hetzner.com/cloud-singapore/), [CPX sizes](https://www.hetzner.com/cloud-made-in-germany/).

Cloudflare R2 currently lists standard storage at $0.015/GB-month, operation charges, a limited free allowance and no direct R2 egress fee. Other attached metered services can still charge. An application-level quota must cap uploads and requests; an alert is not a guaranteed billing hard stop. If a provider cannot enforce an absolute spend cap, bound inputs and retain enough reserve for delayed usage reporting. [R2 pricing](https://developers.cloudflare.com/r2/pricing/).

## Measure the things that create bills

Track host uptime, active simulation minutes, peak process memory, tick time, player-hours, regional bandwidth, tile downloads/cache hits, asset/delta growth, backup growth, AI input/output tokens and cost, support minutes and subscription refunds. One dollar per account is not a useful cost model when one account generates thousands of physics bodies and another visits once a week.

Useful planning equations:

- Player-hours = active users × sessions per active user × average session hours.
- Average concurrency = monthly player-hours ÷ monthly hours. Peak concurrency must be measured separately; it is never safely inferred from the average alone.
- Replication egress GiB = player-hours × average outbound KiB/second × 3,600 ÷ 1,048,576, plus protocol/retransmission overhead.
- Tile egress = uncached sessions × average uncached session payload, plus updates and prefetch waste.
- Stored world data = shared base + sum of unique world deltas + unique assets + backups/logs.
- AI cost = input tokens × input rate + output tokens × output rate + tools/media charges, with units converted to the provider's quoted scale.

Illustration, not a forecast: 40 weekly active people × two one-hour sessions/week × 4.3 weeks = 344 player-hours/month. At 30 KiB/s outbound state each, that is about 35.4 GiB before overhead and terrain. It averages less than one connected player but could produce a 20-person Friday-night queue. Conversely eight continuously connected players at that rate consume about 593 GiB (637 GB decimal) per 30-day month before terrain. Distinguish total accounts, weekly active users, peak concurrent users and active worlds in every report.

AI must stop at the smaller of its dollar cap and its per-account/session limits. Reserve maximum possible request cost before starting a job, allow at most two attempts per creation session, and bound output tokens. Use canned/manual creation when the allowance is exhausted or the provider fails. Do not fund a local inference server or cloud GPU for this alpha. Provider choice is revisited with a fixed test set of requests and measured success per dollar; no inference benchmark was run for this plan.

## When to introduce payment

The following decision gates are hypotheses selected for a very small founder-led service, not industry benchmarks or statistically strong proof of product-market fit.

| Gate | Evidence to collect | Decision |
|---|---|---|
| Discovery | Five conversations and three observed co-creation sessions; note motives and obstacles | Refine the loop and proposed paid service; no checkout |
| Free closed alpha | At least 20 activated testers across cohorts, four weekly sessions, concrete save/return behavior | Improve onboarding and reliability before monetization |
| Repeat value | In a cohort of 20 activated testers, at least six return in week four; at least five repeatedly use a saved creation with another person | Interview returners and non-returners; small denominators require qualitative judgment |
| Willingness to pay | At least five users explicitly choose a specific service and price after seeing the working free/paid comparison | Offer a limited monthly pilot if reliability and billing gates pass |
| Paid pilot | Two billing cycles; actual costs within budget, no unresolved data-loss incidents, manageable support, cancellation works | Keep, revise or stop the offer; do not expand solely on gross revenue |
| Broader offer | Stable paid feature usage, retention, positive cash contribution and measured capacity | Increase invitations and capacity gradually |

Activated means completing the core loop, not merely registering. Week-four return means at least one meaningful session during days 22–28 after activation. Report the numerator and denominator, with exclusions such as test accounts. A 6/20 result is directional information, not proof of a universal 30% retention rate.

If only two enthusiastic users want to support the project, an honestly described supporter payment may be possible after billing readiness, but do not present it as validation of hosted-world economics. Never sell annual or lifetime hosting before its operational cost is understood. Do not pre-sell a complete planet, global player counts or independent federation as a funded promise.

## Initial package hypotheses

Launch at most one paid operational package. More tiers create entitlement code, support questions and price discrimination work before there is evidence of different needs.

| Offer | Draft price to test | Actual value | Prerequisite |
|---|---:|---|---|
| Free | $0 | Shared exploration, manual creation, saved bounded sandbox, owner plus three visitors within service cap | Core MVP and affordable admission policy |
| Optional supporter | About $5/month | Cosmetic recognition and transparent development updates, only if requested | Billing/support readiness; no implied greater world authority |
| Creator service, preferred pilot | $8–$12/month; test $10 | More saved projects or storage, version history/restore and a reserved hosted session window; features actually delivered | Measured per-user cost, backups/restore, cancellation and capacity |
| Later group hosting | Price from measured cost, not guessed now | Larger measured visitor allocation, group roles, longer operation | Reliable multi-region operation and enough revenue to support it |

The exact free quotas and paid uplift are not fixed here. Test whether a 100 MiB free save quota fits real creative activity. A reserved session consumes the same hard platform capacity; define actual weekly bookable sandbox-hours and reserve both a worker slot and seats against the combined eight-player cap. Initially sell no more than half those hours so free access remains meaningful. Five customers cannot all be promised the same Friday evening. Additional saved projects do not imply simultaneous simulations. Cosmetic recognition should not create an advertising surface or allow deceptive impersonation.

Do not add an item marketplace, cash-out, paid randomness, property speculation or transferable real-money land ownership to the MVP. Each adds fraud, disputes and economic authority problems that would compete with building a good game. Commerce can be evaluated later with professional advice and actual user demand.

## Unit economics without disguising unpaid work

For an illustrative US domestic-card subscription, Stripe's published base rate is 2.9% + $0.30 per successful transaction; pay-as-you-go Billing adds 0.7% of billing volume. International cards, currency conversion, taxes, disputes and other services can change the effective cost. The calculation below assumes those two fees only, not a universal payment rate. [Stripe pricing](https://stripe.com/pricing).

Example assumptions: $10/month subscription; net after modeled payment fees $9.34; incremental paid-user operating cost $1.50; refund/uncertainty reserve $1.00; fixed monthly operating cost $80. Contribution toward fixed costs and development is then $6.84 per payer. Cash operating break-even under those assumptions is ceiling($80 ÷ $6.84) = 12 payers. This is not a salary or full business break-even.

| Payers | Gross monthly receipts | After modeled payment fees | After $1.50/user incremental cost and $1/user reserve | Remaining after $80 fixed cost |
|---:|---:|---:|---:|---:|
| 5 | $50 | $46.70 | $34.20 | -$45.80 |
| 10 | $100 | $93.40 | $68.40 | -$11.60 |
| 20 | $200 | $186.80 | $136.80 | $56.80 |
| 30 | $300 | $280.20 | $205.20 | $125.20 |
| 100 | $1,000 | $934.00 | $684.00 | $604.00 |

This algebra holds fixed costs constant only to show the relationship. One hundred paying users may require more server capacity and much more support; re-estimate before selling that many subscriptions. It omits founder wages, taxes on profits, acquisition, specialist fees and changing usage. A sensitivity check matters more than the optimistic row: at $4 incremental usage cost, the same assumptions leave $4.34 per payer and require 19 payers to cover $80. At $8 usage cost, contribution is only $0.34 and the offer needs redesign.

Measure at least support minutes per active creator and per paying creator. Twenty customers each needing 30 minutes a month consume ten founder hours. Agents may assist triage and drafting, but cannot turn human community management and billing responsibility into zero cost.

## Reinvesting all proceeds

Retain all business proceeds for the project. Apply incoming receipts in this order: payment/tax/refund obligations; current hosting, backups and support commitments; a cash reserve of three months of committed operating costs; then additional development. These are all project expenditures or retained project funds. Gross receipts are not all immediately spendable on more agents.

For spendable development cash after those obligations, a starting allocation could be 50% reliability/performance, 30% improving the tested creative loop and 20% measured experiments. Revisit this split monthly against the largest observed blocker; it is not a permanent rule. Early specialist purchases should be narrow reviews of security/networking or a reusable art kit, not expansion into many services.

Capacity spending is triggered by repeated queues for otherwise healthy sessions and positive measured contribution, not by a milestone number of registered accounts. Wait for two stable billing cycles before adding recurring commitments, and retain the ability to reduce them without losing saved worlds. Do not treat projected future subscriptions as permission to exceed the founder's current $100 ceiling. Increasing the ceiling becomes a deliberate budget decision supported by actual net receipts.

## Billing and operational readiness

Before accepting recurring payment, design a concrete service description, cancellation flow, expiration/grace behavior, support contact, refund procedure and incident communication. Use hosted payment entry, avoid storing card data, verify payment notifications and reconcile duplicate/out-of-order events into idempotent entitlements. A browser's checkout-success page must not grant an entitlement by itself.

Test payment failure, cancellation mid-cycle, charge reversal, duplicate webhook, delayed webhook, provider outage, renewal after suspension and restoration of a saved world. Keep an export path and distinguish saved data from active hosted entitlement. Failed payment should not instantly erase a home or sandbox.

Before a public paid launch, arrange review appropriate to the operating location and intended audience for privacy, children/minors, taxes, recurring billing, consumer terms, content rights and moderation. This is a task and budget dependency, not a claim that a generic template establishes compliance. Start the private research cohort with adults, as a proposed scope reduction; the eventual age policy remains a founder decision.

## Business work packages

| Package | Deliverable | Completion evidence |
|---|---|---|
| BIZ-01 cost model | Editable inputs for host, hours, bandwidth, storage, AI and support | Actual first month compared with estimates; alerts tested |
| BIZ-02 player research | Interview guide and anonymized cohort notes | Five interviews and three observed shared sessions |
| BIZ-03 service test | One free/paid comparison and price interview | Five concrete purchase intentions; reasons documented |
| BIZ-04 entitlement plan | Billing state machine and entitlement ledger | Failure/retry/cancellation cases pass before real charging |
| BIZ-05 launch readiness | Service terms, privacy/content review, support and incident runbook | Founder confirms deliverable claims and operating ability |
| BIZ-06 reinvestment review | Monthly cash and capacity report | Actual net funds support the next expense with reserve intact |

The business decision to resolve first is whether players value returning to a shared place and revisiting inventions. If they enjoy a creation once and never return, a subscription should wait while the product loop improves.
