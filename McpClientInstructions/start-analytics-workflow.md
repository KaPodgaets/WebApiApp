# 1 Start Analytics Workflow For Power BI

Use this workflow when the end user asks a Financial Analytics question or request.

## Goal

Guide the MCP client from authentication to required knowledge discovery, schema discovery, query generation, and query execution.

## Workflow

1. Authenticate in Microsoft Entra ID first.
   - If the current MCP session is not signed in yet, call `ms_sign_in`.
   - Check progress with `verify_powerbi_authentication`.
   - Continue only after a valid access token is available for the current MCP session.

2. Determine the business theme of the user's request.
   - Supported themes include `BvA`, `Revenue Analysis`, `Expenses`, `Vendor Bills`, `Customer Payments`, `Balance Sheet`, and `Profit and Loss`.
   - If the request is ambiguous, infer the best match from the wording.
   - If the theme still cannot be determined safely, ask the user for a short clarification.

3. Fetch the required model knowledge from this MCP.
   - Call `get_required_model_knowledge`.
   - Use the returned guidance to identify allowed measures, dimensions, default filters, and business rules.
   - If the required knowledge tool is not exposed, stop and report that the required model knowledge is unavailable.

4. Discover the relevant semantic model schema.
   - Call `powerbi_get_semantic_model_schema` for the target artifact when schema details are needed.
   - Use the schema to confirm the correct semantic model objects and available fields.

5. Follow the retrieved knowledge to prepare the query.
   - Keep the query aligned with the chosen theme and the user's wording.

6. Craft a relevant DAX query.
   - Produce a DAX query that answers the user's analytical request directly.
   - Prefer precise filters, valid measure usage, and a shape that is easy to interpret in the final response.

7. Execute the query through this MCP.
   - Run the resulting DAX with `powerbi_execute_dax_rest` or another appropriate execution tool exposed by this MCP.
   - Return the result in a concise business-friendly format.

## Expected Behavior

- Always authenticate before trying to access protected analytical tools.
- Always choose a theme before selecting model knowledge.
- Always use the model knowledge before writing DAX.
- If any required dependency is missing, fail clearly and explain which step is blocked.
