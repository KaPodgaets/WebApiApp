# Get Required Financial Analytics Model Knowledge

Use this knowledge package to plan and build DAX queries for the Financial Analytics semantic model.

---

# Models:
## Financial Analytics model / FI Analytics model
Covered themes:
- `BvA`
- `Revenue Analysis`
- `Expenses`
- `Vendor Bills`
- `Customer Payments`
- `Balance Sheet`
- `Profit and Loss`

workspace id - be12499c-6e33-4575-9535-c66fe0764859

artifact id (model id) - f1a550a2-2a82-442f-aa88-64db229b6d51

## How To Apply This Knowledge

1. Identify the business intent in the user request.
2. Match the request to one of the allowed measures below.
3. Select only dimensions that are listed for that measure.
4. Build the DAX query using the chosen measure and dimensions.
5. If multiple measures seem possible, prefer the one whose definition most directly matches the user's wording.
6. If a requested metric is not covered here, stop and ask for updated Financial Analytics model knowledge.
7. If the requested dimension is not listed for the selected measure, do not use a similar-looking dimension. Ask for updated model knowledge or clarification.

## Global Query Rules

- Use only measures, tables, columns, calculation groups, and filters explicitly listed in this document.
- Never invent measure names, table names, column names, relationships, hierarchies, or filters.
- Prefer existing model measures over manually calculated expressions.

## Stop Rules

- If the user requests a metric not listed in this document, stop and ask for updated model knowledge.
- If the user requests a dimension not listed for the chosen measure, stop and ask for clarification or updated model knowledge.
