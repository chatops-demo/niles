using System;
using System.Threading;
using System.Threading.Tasks;
using BasicBot.Services;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;

namespace BasicBot.Dialogs.CreateIssues
{
    public class CreateIssueDialog : ComponentDialog
    {
        public IStatePropertyAccessor<CreateIssueState> IssueRequestAccessor { get; }

        private JobService _jobService;
        private ProbotService _probotService;
        private const string IssueDialog = "issueDialog";

        private struct Prompts
        {
            public const string RepoName = "repoName";
            public const string IssueTitle = "issueTitle";
            public const string IssueBody = "issueBody";
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CreateIssueDialog"/> class.
        /// </summary>
        /// <param name="botServices">Connected services used in processing.</param>
        /// <param name="botState">The <see cref="UserState"/> for storing properties at user-scope.</param>
        /// <param name="loggerFactory">The <see cref="ILoggerFactory"/> that enables logging and tracing.</param>
        public CreateIssueDialog(IStatePropertyAccessor<CreateIssueState> issueRequestAccessor, ILoggerFactory loggerFactory, JobService jobService, ProbotService probotService)
            : base(nameof(CreateIssueDialog))
        {
            IssueRequestAccessor = issueRequestAccessor ?? throw new ArgumentNullException(nameof(issueRequestAccessor));
            _jobService = jobService ?? throw new ArgumentNullException(nameof(jobService));
            _probotService = probotService ?? throw new ArgumentNullException(nameof(probotService));

            // Add control flow dialogs
            var waterfallSteps = new WaterfallStep[]
            {
                    InitializeStateStepAsync,
                    PromptForRepoNameStepAsync,
                    PromptForIssueTitleStepAsync,
                    PromptForIssueBodyStepAsync,
                    DisplayRequestStateStepAsync,
            };
            AddDialog(new WaterfallDialog(IssueDialog, waterfallSteps));
            AddDialog(new TextPrompt(Prompts.RepoName));
            AddDialog(new TextPrompt(Prompts.IssueTitle));
            AddDialog(new TextPrompt(Prompts.IssueBody));
        }

        private async Task<DialogTurnResult> InitializeStateStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var issueRequestState = await IssueRequestAccessor.GetAsync(stepContext.Context, () => null);
            if (issueRequestState == null)
            {
                var issueRequestStateOpt = stepContext.Options as CreateIssueState;
                if (issueRequestStateOpt != null)
                {
                    await IssueRequestAccessor.SetAsync(stepContext.Context, issueRequestStateOpt);
                }
                else
                {
                    await IssueRequestAccessor.SetAsync(stepContext.Context, new CreateIssueState());
                }
            }

            return await stepContext.NextAsync();
        }

        private async Task<DialogTurnResult> PromptForRepoNameStepAsync(
                                                WaterfallStepContext stepContext,
                                                CancellationToken cancellationToken)
        {
            var issueRequestState = await IssueRequestAccessor.GetAsync(stepContext.Context);

            if (string.IsNullOrWhiteSpace(issueRequestState.RepoName))
            {
                // prompt for repo name, if missing
                var opts = new PromptOptions
                {
                    Prompt = new Activity
                    {
                        Type = ActivityTypes.Message,
                        Text = "In what repo would you like to create the issue?",
                    },
                };
                return await stepContext.PromptAsync(Prompts.RepoName, opts);
            }
            else
            {
                return await stepContext.NextAsync();
            }
        }

        private async Task<DialogTurnResult> PromptForIssueTitleStepAsync(
                                                WaterfallStepContext stepContext,
                                                CancellationToken cancellationToken)
        {
            var issueRequestState = await IssueRequestAccessor.GetAsync(stepContext.Context);

            // save repoName from previous step
            var repoName = stepContext.Result as string;
            if (string.IsNullOrWhiteSpace(issueRequestState.RepoName) && repoName != null)
            {
                issueRequestState.RepoName = repoName;
                await IssueRequestAccessor.SetAsync(stepContext.Context, issueRequestState);
            }

            if (string.IsNullOrWhiteSpace(issueRequestState.IssueTitle))
            {
                // prompt for issue title, if missing
                var opts = new PromptOptions
                {
                    Prompt = new Activity
                    {
                        Type = ActivityTypes.Message,
                        Text = "What should the title of the issue be?",
                    },
                };
                return await stepContext.PromptAsync(Prompts.IssueTitle, opts);
            }
            else
            {
                return await stepContext.NextAsync();
            }
        }

        private async Task<DialogTurnResult> PromptForIssueBodyStepAsync(
                                                WaterfallStepContext stepContext,
                                                CancellationToken cancellationToken)
        {
            var issueRequestState = await IssueRequestAccessor.GetAsync(stepContext.Context);

            // Save issue title from prev step
            var issueTitle = stepContext.Result as string;
            if (string.IsNullOrWhiteSpace(issueRequestState.IssueTitle) && issueTitle != null)
            {
                issueRequestState.IssueTitle = issueTitle;
                await IssueRequestAccessor.SetAsync(stepContext.Context, issueRequestState);
            }

            if (string.IsNullOrWhiteSpace(issueRequestState.IssueBody))
            {
                // prompt for issue body, if missing
                var opts = new PromptOptions
                {
                    Prompt = new Activity
                    {
                        Type = ActivityTypes.Message,
                        Text = "What would you like to include in the body?",
                    },
                };
                return await stepContext.PromptAsync(Prompts.IssueBody, opts);
            }
            else
            {
                return await stepContext.NextAsync();
            }
        }

        private async Task<DialogTurnResult> DisplayRequestStateStepAsync(
                                                    WaterfallStepContext stepContext,
                                                    CancellationToken cancellationToken)
        {
            // Save city, if prompted.
            var issueRequestState = await IssueRequestAccessor.GetAsync(stepContext.Context);

            var issueBody = stepContext.Result as string;
            if (string.IsNullOrWhiteSpace(issueRequestState.IssueBody) &&
                !string.IsNullOrWhiteSpace(issueBody))
            {
                issueRequestState.IssueBody = issueBody;
                await IssueRequestAccessor.SetAsync(stepContext.Context, issueRequestState);
            }

            return await DisplaySelections(stepContext);
        }

        // Helper function to display user with selections.
        private async Task<DialogTurnResult> DisplaySelections(WaterfallStepContext stepContext)
        {
            var context = stepContext.Context;
            var issueRequestState = await IssueRequestAccessor.GetAsync(context);

            // Display their request
            await context.SendActivityAsync($"Creating new issue with title: {issueRequestState.IssueTitle}\r\n" +
                $"And body: {issueRequestState.IssueBody}\r\nIn the {issueRequestState.RepoName} repo.");

            // Create and save job
            string conversationId = await _jobService.StartJob(context);

            // Post to probot post(conversationId, issueRequestState)
            // TODO:
            var obj = new IssueDTO()
            {
                conversationId = conversationId,
                issue = issueRequestState.IssueTitle,
            };

            await _probotService.PostIssue(obj);

            // clear issueRequestState for the next call
            await IssueRequestAccessor.SetAsync(stepContext.Context, null);

            return await stepContext.EndDialogAsync();
        }
    }
}
