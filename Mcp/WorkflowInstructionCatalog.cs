namespace WebApiApp.Mcp;

public sealed class WorkflowInstructionCatalog(IHostEnvironment hostEnvironment)
{
    private readonly string _instructionsDirectory = Path.Combine(
        hostEnvironment.ContentRootPath,
        "McpClientInstructions");

    public WorkflowInstructionDocument GetStartFinancialAnalyticsWorkflowInstruction()
    {
        return LoadInstruction(
            fileName: "start-analytics-workflow.md",
            instructionName: "start_financial_analytics_workflow",
            title: "Start Financial Analytics Workflow");
    }

    public WorkflowInstructionDocument GetRequiredSemanticModelKnowledgeInstruction(string modelName)
    {
        var normalizedModelName = NormalizeModelName(modelName);
        return normalizedModelName switch
        {
            "financial_analytics" => LoadInstruction(
                fileName: "financial-analytics-model-knowledge.md",
                instructionName: "get_required_semantic_model_knowledge",
                title: "Get Required Semantic Model Knowledge"),
            "another_model" => LoadInstruction(
                fileName: "another-model-knowledge.md",
                instructionName: "get_required_semantic_model_knowledge",
                title: "Get Required Semantic Model Knowledge"),
            _ => throw new InvalidOperationException(
                $"Unsupported model '{modelName}'. Supported models: Financial Analytics, Another model.")
        };
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

    private static string NormalizeModelName(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            throw new InvalidOperationException(
                "A model name is required. Supported models: Financial Analytics, Another model.");
        }

        var normalized = modelName.Trim().ToLowerInvariant();
        return normalized switch
        {
            "financial analytics" => "financial_analytics",
            "finance analytics" => "financial_analytics",
            "fi analytics" => "financial_analytics",
            "another model" => "another_model",
            _ => normalized.Replace(' ', '_')
        };
    }
}

public sealed record WorkflowInstructionDocument(
    string Name,
    string Title,
    string FileName,
    string Markdown);
