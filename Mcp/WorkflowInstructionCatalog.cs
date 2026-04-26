namespace WebApiApp.Mcp;

public sealed class WorkflowInstructionCatalog(IHostEnvironment hostEnvironment)
{
    private readonly string _instructionsDirectory = Path.Combine(
        hostEnvironment.ContentRootPath,
        "McpClientInstructions");

    public WorkflowInstructionDocument GetDiscoverWorkflowInstruction()
    {
        return LoadInstruction(
            fileName: "discover-workflow.md",
            instructionName: "discover_workflow",
            title: "Discover Workflow");
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
