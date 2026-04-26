using Microsoft.AspNetCore.Mvc;

namespace WebApiApp.Controllers;

[ApiController]
[Route("api/mcp")]
public sealed class McpController : ControllerBase
{
    [HttpGet]
    public IActionResult GetInfo()
    {
        return Ok(new
        {
            server = "web-api-app",
            endpoint = "/mcp",
            transport = "streamable-http",
            tools = new[]
            {
                "start_financial_analytics_workflow",
                "mcp_echo_status",
                "ms_sign_in",
                "verify_powerbi_authentication",
                "powerbi_get_semantic_model_schema",
                "get_required_semantic_model_knowledge",
                "powerbi_list_workspaces_and_models_rest",
                "powerbi_execute_dax_rest"
            }
        });
    }
}
