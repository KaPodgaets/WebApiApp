# MCP Tool Naming Convention

Use this naming convention for Financial Analytics MCP tools that guide an agent through a fixed workflow.

## Convention

- Use a `step_N_` prefix when documenting the expected sequence for agentic workflow tools.
- Keep the rest of the tool name action-oriented and domain-specific.
- Prefer names that describe the concrete outcome the agent should achieve at that step.

## Recommended Sequence

- `step_1_start_financial_analytics_workflow`
- `step_2_verify_powerbi_authentication`
- `step_3_get_required_financial_analytics_model_knowledge`
- `step_4_fetch_powerbi_semantic_model_schema`
- `step_5_execute_powerbi_dax_query`

## Notes

- Actual exposed MCP tool names may omit the `step_N_` prefix when a shorter public tool name is preferred.
- The sequence above should still be treated as the canonical agentic workflow order.
