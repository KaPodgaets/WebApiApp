# Financial Analytics Semantic Model Knowledge — BvA (FI Analytics)

## Purpose

Use this knowledge package to plan and build DAX queries for Budget vs Actual analysis in the Financial Analytics semantic model.

## Model ids
workspace id - be12499c-6e33-4575-9535-c66fe0764859
artifact id - f1a550a2-2a82-442f-aa88-64db229b6d51

---
# How To Apply This Knowledge

1. Identify the business intent in the user request.
2. Match the request to one of the allowed measures below.
3. Select only dimensions that are listed for that measure.
4. Build the DAX query using the chosen measure and dimensions.
5. If multiple measures seem possible, prefer the one whose definition most directly matches the user's wording.
6. If a requested metric is not covered here, stop and ask for updated Financial Analytics model knowledge.
7. If the requested dimension is not listed for the selected measure, do not use a similar-looking dimension. Ask for updated model knowledge or clarification.

---

# Supported Business Area

## BvA — Budget vs Actual

Use this area when the user asks about:

- budget vs actual
- BvA
- actual vs budget
- budget variance
- variance to plan
- gap to budget
- over budget
- under budget
- performance against budget
- KPI performance against plan
- actual, budget, and variance comparison

---

# Global Query Rules

- Use only measures, tables, columns, calculation groups, and filters explicitly listed in this document.
- Never invent measure names, table names, column names, relationships, hierarchies, or filters.
- Prefer existing model measures over manually calculated expressions.
- Do not manually calculate variance if a variance measure is listed.
- Do not manually calculate variance percentage if a variance percentage measure is listed.
- Use only dimensions listed as allowed BvA dimensions.
- If the user requests an unsupported metric, retrieve updated model knowledge or ask for clarification.
- If the user requests an unsupported dimension, retrieve updated model knowledge or ask for clarification.
- If the request is not about BvA, do not answer from this knowledge package.

---

# Mandatory Default Filters

Unless the user explicitly requests otherwise, every DAX query must apply:

- `'Currency'[Currency Code] = "USD"`
- `'GAAP'[Calculation group] = "GAAP"`

The user may override these defaults only by explicitly requesting a different currency or accounting basis.

---

## Multi-Measure Dimension Rule

When a query uses multiple measures, use only dimensions that are allowed for all selected measures.

If the requested dimension is not allowed for one of the selected measures, do not build the query. Ask for clarification or retrieve updated model knowledge.

# BvA Measure Catalog

## Core BvA Measures

### `Actual Amount`

Business metric: Actual Amount
DAX measure: `'1 All Measures'[Actual]`

Business meaning:

Actual realized amount for the selected context.

Use for:

- actuals
- actual amount
- booked amount
- realized amount
- current performance
- actual result

---

### `Budget Amount`

Business metric: Budget Amount
DAX measure: `'1 All Measures'[Budget]`

Business meaning:

Budgeted or planned amount for the selected context.

Use for:

- budget
- plan
- planned amount
- target amount
- baseline
- expected amount

---

### `Actual vs Budget`

Business metric: Actual vs Budget
DAX measure: `'1 All Measures'[Act vs Bud]`

Business meaning:

Absolute variance between actual and budget according to the model-defined BvA logic.

Use for:

- variance
- budget variance
- actual vs budget
- gap to budget
- difference from budget
- over budget
- under budget
- overperformance
- underperformance

---

### `Actual vs Budget %`

Business metric: Actual vs Budget %
DAX measure: `'1 All Measures'[% Act vs Bud]`

Business meaning:

Percentage variance between actual and budget according to the model-defined BvA logic.

Use for:

- variance %
- budget variance %
- percentage variance
- actual vs budget %
- performance percentage
- percent over budget
- percent under budget

---

# BvA Measure Bundles

Use measure bundles to answer common BvA analytical questions.

A measure bundle is the default set of measures that should be returned for a business scenario.

---

## Bundle: `BvA Overview`

Use when the user asks for general BvA, budget variance, KPI performance, or performance against plan.

User wording examples:

- "show BvA"
- "show budget vs actual"
- "show performance against budget"
- "show KPI performance"
- "show actual vs budget by department"
- "where are we over or under budget?"

Use these measures:

- `Actual Amount`
- `Budget Amount`
- `Actual vs Budget`
- `Actual vs Budget %`

This is the default bundle for BvA questions.

---

# Allowed BvA Dimensions

Use only these dimensions for BvA queries:

## Date dimensions

- `Calendar[Month]`
- `Calendar[Quarter]`
- `Calendar[Year]`
- `Calendar[Year Month]`

## Organization dimensions

- `Departments[Department]`
- `Departments[Sub Department]`

---

# Default Time Grain Rules

Use these defaults only when the user asks for a trend or time-based analysis without specifying the exact grain.

- "trend" → `Calendar[Month]`
- "monthly" → `Calendar[Month]`
- "quarterly" → `Calendar[Quarter]`
- "yearly" / "annual" → `Calendar[Year]`
- "over time" → `Calendar[Month]`

If the user asks for a summary and does not request a time breakdown, do not add a date dimension automatically.

---

# DAX Query Construction Rules

- Use `SUMMARIZECOLUMNS` for grouped analytical queries.
- Apply mandatory default filters inside `SUMMARIZECOLUMNS`.
- Include only selected measure bundle measures.
- Include only requested or required dimensions.
- Do not create calculated measures inside the DAX query.
- Do not manually calculate `Actual vs Budget`.
- Do not manually calculate `Actual vs Budget %`.
- Use existing measures exactly as named in this document.
- Do not use columns outside the allowed BvA dimensions.

---

# Example Query Patterns

## General BvA by Department

User request:

"Show BvA by department."

Selected bundle:

`BvA Overview`

Selected dimensions:

- `Departments[Department]`

Selected measures:

- `Actual Amount`
- `Budget Amount`
- `Actual vs Budget`
- `Actual vs Budget %`

Expected DAX shape:

```DAX
EVALUATE
SUMMARIZECOLUMNS(
    'Departments'[Department],

    TREATAS({"USD"}, 'Currency'[Currency]),
    TREATAS({"GAAP"}, 'GAAP'[Calculation group column]),

    "Actual", '1 All Measures'[Actual],
    "Budget", '1 All Measures'[Budget],
    "Act vs Bud", '1 All Measures'[Act vs Bud],
    "% Act vs Bud", '1 All Measures'[% Act vs Bud]
)
```

---

## Answer Rules

- Base the final answer only on returned query results.
- Do not infer values that were not returned by the query.
- If the query result is empty, say that no data was returned for the selected filters.
- If a requested breakdown is unsupported, say which dimension is unsupported and list the allowed BvA - dimensions.
- If the user asked for general BvA, explain actual, budget, variance, and variance percentage together.
- If the user asked for over-budget or under-budget items, sort or interpret by Actual vs Budget unless the user requested percentage ranking.

## Ambiguity and Stop Rules
Ask for clarification or retrieve updated model knowledge when:

- The user requests a metric not listed in this document.
- The user requests a dimension not listed in allowed BvA dimensions.
- The user asks about a non-BvA business area.
- The user asks for a calculation that requires unavailable measures.
- The user asks for a KPI but does not make clear that it should be evaluated against budget or plan.

Do not ask for clarification when the request clearly maps to one of the BvA measure bundles.