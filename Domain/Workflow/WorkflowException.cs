namespace Domain.Workflow;

/// <summary>
/// Thrown when a state transition is not permitted by the workflow.
/// </summary>
public sealed class WorkflowException : Exception
{
    public WorkflowException(string message) : base(message) { }
}
