using Domain.Repositories;
using Domain.Tickets;
using Domain.Workflow;
using GoogleChatBot.Cards;
using Infrastructure.Analysis;
using Infrastructure.Fix;
using Infrastructure.Google;
using Microsoft.Extensions.Options;

namespace GoogleChatBot.Services;

/// <summary>
/// Runs the long-running LLM pipelines (analysis and fix) for a ticket in the background.
/// Each method is intended to be fire-and-forgotten from <c>ActionController</c>.
/// </summary>
public sealed class TicketPipelineRunner(
    IAnalysisService analysis,
    IFixService fix,
    ITicketRepository repo,
    TicketWorkflow workflow,
    IGoogleChatApiService chatApi,
    IGoogleDriveService drive,
    ITicketThreadNotifier notifier,
    IOptions<GoogleCredentialOptions> credentialOptions,
    ILogger<TicketPipelineRunner> logger) : ITicketPipelineRunner
{
    private readonly string _inProcessFolderId = credentialOptions.Value.InProcessFolderId;
    private readonly string _doneFolderId = credentialOptions.Value.DoneFolderId;

    public async Task RunAnalysisAsync(ErrorTicket ticket)
    {
        try
        {
            var report = await analysis.AnalyzeAsync(ticket);

            ticket.AnalysisResult = report;
            ticket.UpdatedAt = DateTimeOffset.UtcNow;
            workflow.Transition(ticket, TicketState.Analyzed);

            await repo.UpdateAsync(ticket);

            var truncated = report.Length > 3500 ? report[..3500] + "\n\n_(truncated)_" : report;

            await chatApi.PostThreadReplyAsync(
                ticket.SpaceName!, ticket.ThreadName!,
                $"🔍 *Analysis complete:*\n\n{truncated}");

            await chatApi.PostThreadMessageAsync(
                ticket.SpaceName!, ticket.ThreadName!,
                TicketCardBuilder.Build(ticket, workflow));

            logger.LogInformation("Analysis posted for ticket {Id}", ticket.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background analysis failed for ticket {Id}", ticket.Id);

            await notifier.MarkFailedAsync(ticket, $"❌ Analysis failed: {ex.Message}");
        }
    }

    public async Task RunFixAsync(ErrorTicket ticket)
    {
        try
        {
            var branchName = await fix.ApplyFixAsync(ticket);

            ticket.BranchName = branchName;
            ticket.UpdatedAt = DateTimeOffset.UtcNow;
            workflow.Transition(ticket, TicketState.Fixed);

            await repo.UpdateAsync(ticket);
            await chatApi.PostThreadReplyAsync(
                ticket.SpaceName!, ticket.ThreadName!,
                $"🔧 Fix committed to branch `{branchName}`. Review and merge when ready.");
            await chatApi.PostThreadMessageAsync(
                ticket.SpaceName!, ticket.ThreadName!,
                TicketCardBuilder.Build(ticket, workflow));
            await MoveFileToDoneAsync(ticket);

            logger.LogInformation("Fix posted for ticket {Id}, branch {Branch}", ticket.Id, branchName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background fix failed for ticket {Id}", ticket.Id);

            await notifier.MarkFailedAsync(ticket, $"❌ Fix pipeline failed: {ex.Message}");
        }
    }
    private async Task MoveFileToDoneAsync(ErrorTicket ticket)
    {
        if (string.IsNullOrEmpty(ticket.SourceFileId) ||
            string.IsNullOrEmpty(_inProcessFolderId)  ||
            string.IsNullOrEmpty(_doneFolderId))
        {
            return;
        }

        try
        {
            await drive.MoveFileAsync(
                ticket.SourceFileId,
                _inProcessFolderId,
                _doneFolderId,
                CancellationToken.None);

            logger.LogInformation("File {FileId} moved to Done/ for ticket {Id}", ticket.SourceFileId, ticket.Id);
        }
        catch (Exception ex)
        {
            // Non-fatal — fix is already committed; log and continue.
            logger.LogWarning(ex,
                "Failed to move file {FileId} to Done/ for ticket {Id}",
                ticket.SourceFileId, ticket.Id);
        }
    }
}
