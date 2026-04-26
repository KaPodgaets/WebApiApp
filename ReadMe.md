# MCP client flow
- start flow (by fetching mcp tool - start_financial_analytics_workflow)
- check auth MS Entra ID status (fetch mcp tool - verify_powerbi_authentication)
    - invoke ms_sign_in if needed
- invoke "get_required_semantic_model_knowledge" (mcp client has to send what model from the list: like HR, FI Analytics etc)
- fetch semantic model schema (invoke - powerbi_get_semantic_model_schema)
- create DAX query (on Client side)
- execute DAX query (invoke tool)
- craft answer (on Client side)