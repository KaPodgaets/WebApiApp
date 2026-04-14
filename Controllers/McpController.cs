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
                "sum_digits",
                "multiply_digits",
                "get_utc_datetime",
                "get_client_app_id",
                "mcp_echo_ok",
                "mcp_echo_status",
                "ms_sign_in",
                "ms_sign_in_status",
                "ms_sign_in_status_minimal",
                "powerbi_get_semantic_model_schema",
                "powerbi_generate_query",
                "powerbi_execute_query",
                "powerbi_execute_dax_rest"
            }
        });
    }
}
