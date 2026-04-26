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
                "discover_workflow",
                "mcp_echo_status",
                "ms_sign_in",
                "ms_sign_in_status",
                "powerbi_get_semantic_model_schema",
                "powerbi_list_workspaces_and_models_rest",
                "powerbi_execute_dax_rest"
            }
        });
    }
}
