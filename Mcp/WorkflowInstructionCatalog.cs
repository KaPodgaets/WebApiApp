namespace WebApiApp.Mcp;

public sealed class WorkflowInstructionCatalog(IHostEnvironment hostEnvironment)
{
    private readonly string _instructionsDirectory = Path.Combine(
        hostEnvironment.ContentRootPath,
        "McpClientInstructions");

    public WorkflowInstructionDocument GetStartFinancialAnalyticsWorkflowInstruction()
    {
        return LoadInstruction(
            fileName: "start-financial-analytics-workflow.md",
            instructionName: "start_financial_analytics_workflow",
            title: "Start Financial Analytics Workflow");
    }

    public WorkflowInstructionDocument GetRequiredFinancialAnalyticsModelKnowledgeInstruction()
    {
        return LoadInstruction(
            fileName: "get-required-financial-analytics-model-knowledge.md",
            instructionName: "get_required_financial_analytics_model_knowledge",
            title: "Get Required Financial Analytics Model Knowledge");
    }

    private WorkflowInstructionDocument LoadInstruction(
        string fileName,
        string instructionName,
        string title)
    {
        var fullPath = Path.Combine(_instructionsDirectory, fileName);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"The MCP instruction file '{fileName}' was not found in '{_instructionsDirectory}'.",
                fullPath);
        }

        return new WorkflowInstructionDocument(
            instructionName,
            title,
            fileName,
            File.ReadAllText(fullPath));
    }
}

public sealed record WorkflowInstructionDocument(
    string Name,
    string Title,
    string FileName,
    string Markdown);
