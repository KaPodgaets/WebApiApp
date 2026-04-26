# Discover Workflow

Use this workflow when the end user asks an analytical question or request.

## Goal

Guide the MCP client from authentication to knowledge discovery, query generation, and query execution.

## Workflow

1. Authenticate in Microsoft Entra ID first.
   - If the current MCP session is not signed in yet, call `ms_sign_in`.
   - Check progress with `ms_sign_in_status`.
   - Continue only after a valid access token is available for the current MCP session.

2. Determine the business theme of the user's request.
   - Supported themes are `BvA`, `HR`, and `Marketing`.
   - If the request is ambiguous, infer the best match from the wording.
   - If the theme still cannot be determined safely, ask the user for a short clarification.

3. Fetch the theme-specific knowledge from this MCP.
   - For `HR`, fetch `HR model Knowledge`.
   - For `BvA`, fetch `BvA model knowledge`.
   - For `Marketing`, fetch `Marketing model Knowledge`.
   - If the expected knowledge tool is not exposed, stop and report that the required model knowledge is unavailable.

4. Follow the retrieved knowledge to prepare the query.
   - Use the returned guidance to identify the correct semantic model objects, metrics, filters, and business rules.
   - Keep the query aligned with the chosen theme and the user's wording.

5. Craft a relevant DAX query.
   - Produce a DAX query that answers the user's analytical request directly.
   - Prefer precise filters, valid measure usage, and a shape that is easy to interpret in the final response.

6. Execute the query through this MCP.
   - Run the resulting DAX with the appropriate execution tool exposed by this MCP.
   - Return the result in a concise business-friendly format.

## Expected Behavior

- Always authenticate before trying to access protected analytical tools.
- Always choose a theme before selecting model knowledge.
- Always use the theme knowledge before writing DAX.
- If any required dependency is missing, fail clearly and explain which step is blocked.
