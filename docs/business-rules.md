# NovaTech CRM — Discount Business Rules

_Last updated by Sales Ops — Q3 2023. Finance dispute raised Q1 2024._

---

## The Dispute

Finance and Sales are disagreeing on how discounts should be calculated.

**Sales** says: a customer with both a Contract discount (10%) and a Promotional
discount (20%) should receive both — the engine applies all matching rules.

**Finance** says: only the highest-priority rule should apply. A customer with a
Contract discount gets exactly that — 10% off. The Promotional discount is
irrelevant because a higher-priority rule already matched.

**Finance is correct.** The original design spec (attached) is clear: rules
cascade by priority and only ONE rule is applied per order.

---

## Cascade Priority Rules

| Priority | Category    | Description                              |
|----------|-------------|------------------------------------------|
| 1        | Contract    | Negotiated rate locked in customer MSA   |
| 2        | Promotional | Time-limited campaign (e.g. Black Friday) |
| 3        | Volume      | Tier discount based on order quantity    |
| 4        | Default     | Catch-all discount for all customers     |

**Only the single highest-priority matching active rule is applied.**
Rules do not stack. If a customer matches Contract and Promotional, only
Contract applies.

---

## Example

Customer has two active rules:
- Contract discount: **10% off** (priority 1)
- Promotional discount: **20% off** (priority 2)

| Calculation        | Result  | Correct? |
|--------------------|---------|----------|
| Apply Contract only: $100 × 0.90 | **$90** | ✓ |
| Apply both (current bug): $100 × 0.90 × 0.80 | **$72** | ✗ |

The engine currently applies both rules additively, giving customers more
discount than they are entitled to. Finance discovered this during the Q4
revenue reconciliation — NovaTech is under-charging contracted customers.

---

## Root Cause

The `DiscountEngine.Apply()` method iterates all active rules in a `foreach`
loop without any priority filtering. The correct implementation should:

1. Filter to active rules only
2. Order by `Category` ascending (Contract = 1 is highest priority)
3. Take the **first** (highest-priority) matching rule
4. Apply only that single rule

---

## Additional Rules

- Discounts cannot reduce an order below $0.
- Inactive rules (`IsActive = false`) are always skipped.
- `DiscountPercent` is a whole number (15 = 15%, not 0.15).
