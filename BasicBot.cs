// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

// See https://github.com/microsoft/botbuilder-samples for a more comprehensive list of samples.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BasicBot.Dialogs.CreateIssues;
using BasicBot.Jobs;
using BasicBot.Services;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Configuration;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.BotBuilderSamples
{
    /// <summary>
    /// Main entry point and orchestration for bot.
    /// </summary>
    public class BasicBot : IBot
    {
        // Supported LUIS Intents
        public const string GreetingIntent = "Greeting";
        public const string CreateIssueIntent = "CreateIssue";
        public const string CancelIntent = "Cancel";
        public const string HelpIntent = "Help";
        public const string NoneIntent = "None";

        /// <summary>
        /// Key in the bot config (.bot file) for the LUIS instance.
        /// In the .bot file, multiple instances of LUIS can be configured.
        /// </summary>
        public static readonly string LuisConfiguration = "BasicBotLuisApplication";

        private readonly IStatePropertyAccessor<GreetingState> _greetingStateAccessor;
        private readonly IStatePropertyAccessor<CreateIssueState> _issueStateAccessor;
        private readonly IStatePropertyAccessor<DialogState> _dialogStateAccessor;
        private readonly UserState _userState;
        private readonly ConversationState _conversationState;
        private readonly BotServices _services;
        private readonly JobService _jobService;
        private readonly NotificationService _notificationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="BasicBot"/> class.
        /// </summary>
        /// <param name="botServices">Bot services.</param>
        /// <param name="accessors">Bot State Accessors.</param>
        public BasicBot(BotServices services, JobService jobService, NotificationService notificationService, EndpointService endpointService,
            UserState userState, ConversationState conversationState, ILoggerFactory loggerFactory)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _jobService = jobService ?? throw new ArgumentNullException(nameof(jobService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _userState = userState ?? throw new ArgumentNullException(nameof(userState));
            _conversationState = conversationState ?? throw new ArgumentNullException(nameof(conversationState));

            AppId = string.IsNullOrWhiteSpace(endpointService.AppId) ? "1" : endpointService.AppId;

            _greetingStateAccessor = _userState.CreateProperty<GreetingState>(nameof(GreetingState));
            _issueStateAccessor = _userState.CreateProperty<CreateIssueState>(nameof(CreateIssueState));
            _dialogStateAccessor = _conversationState.CreateProperty<DialogState>(nameof(DialogState));

            // Verify LUIS configuration.
            if (!_services.LuisServices.ContainsKey(LuisConfiguration))
            {
                throw new InvalidOperationException($"The bot configuration does not contain a service type of `luis` with the id `{LuisConfiguration}`.");
            }

            Dialogs = new DialogSet(_dialogStateAccessor);
            Dialogs.Add(new GreetingDialog(_greetingStateAccessor, loggerFactory));
            Dialogs.Add(new CreateIssueDialog(_issueStateAccessor, loggerFactory, jobService));
        }

        private DialogSet Dialogs { get; set; }

        private string AppId { get; }

        /// <summary>
        /// Run every turn of the conversation. Handles orchestration of messages.
        /// </summary>
        /// <param name="turnContext">Bot Turn Context.</param>
        /// <param name="cancellationToken">Task CancellationToken.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken)
        {
            var activity = turnContext.Activity;

            // Create a dialog context
            var dc = await Dialogs.CreateContextAsync(turnContext);

            if (activity.Type == ActivityTypes.Message)
            {
                // Remove the bot mention to avoid issues with LUIS's NLP
                dc.Context.Activity.RemoveRecipientMention();

                // Perform a call to LUIS to retrieve results for the current activity message.
                var luisResults = await _services.LuisServices[LuisConfiguration].RecognizeAsync(dc.Context, cancellationToken).ConfigureAwait(false);

                // If any entities were updated, treat as interruption.
                // For example, "no my name is tony" will manifest as an update of the name to be "tony".
                var topScoringIntent = luisResults?.GetTopScoringIntent();

                var topIntent = topScoringIntent.Value.intent;

                // update greeting state with any entities captured
                // await UpdateGreetingState(luisResults, dc.Context);

                // Handle conversation interrupts first.
                var interrupted = await IsTurnInterruptedAsync(dc, topIntent);
                if (interrupted)
                {
                    // Bypass the dialog.
                    // Save state before the next turn.
                    await _conversationState.SaveChangesAsync(turnContext);
                    await _userState.SaveChangesAsync(turnContext);
                    return;
                }

                // for testing to view list of jobs. TODO: clean up
                var text = turnContext.Activity.Text.Trim().ToLowerInvariant();
                switch (text)
                {
                    case "show":
                    case "show jobs":
                        await _jobService.ListJobs(turnContext);

                        break;

                    default:
                        break;
                }

                // Continue the current dialog
                var dialogResult = await dc.ContinueDialogAsync();

                // if no one has responded,
                if (!dc.Context.Responded)
                {
                    // examine results from active dialog
                    switch (dialogResult.Status)
                    {
                        case DialogTurnStatus.Empty:
                            switch (topIntent)
                            {
                                case GreetingIntent:
                                    await dc.Context.SendActivityAsync("Hello, this is Niles, your DevOps ChatBot assistant");
                                    break;

                                case CreateIssueIntent:
                                    await dc.BeginDialogAsync(nameof(CreateIssueDialog));
                                    break;

                                case NoneIntent:
                                default:
                                    // Help or no intent identified, either way, let's provide some help.
                                    // to the user
                                    await dc.Context.SendActivityAsync("I didn't understand what you just said to me.");
                                    await DisplayHelp(dc.Context);
                                    break;
                            }

                            break;

                        case DialogTurnStatus.Waiting:
                            // The active dialog is waiting for a response from the user, so do nothing.
                            break;

                        case DialogTurnStatus.Complete:
                            await dc.EndDialogAsync();
                            break;

                        default:
                            await dc.CancelAllDialogsAsync();
                            break;
                    }
                }
            }
            else if (activity.Type == ActivityTypes.ConversationUpdate)
            {
                // Debugging purposes. Remove Later
                await dc.Context.SendActivityAsync("Conversation Update occured");

                if (activity.MembersAdded.Any())
                {
                    // Iterate over all new members added to the conversation.
                    foreach (var member in activity.MembersAdded)
                    {
                        // Debugging purposes. Remove Later
                        await dc.Context.SendActivityAsync($"Member joined {member.Name} with id: {member.Id}");

                        // Greet anyone that was not the target (recipient) of this message.
                        // To learn more about Adaptive Cards, see https://aka.ms/msbot-adaptivecards for more details.
                        if (member.Id != activity.Recipient.Id)
                        {
                            var welcomeCard = CreateAdaptiveCardAttachment(@".\Dialogs\Welcome\Resources\welcomeCard.json");
                            var response = CreateResponse(activity, welcomeCard);
                            await dc.Context.SendActivityAsync(response).ConfigureAwait(false);
                        }
                        else
                        {
                            await dc.Context.SendActivityAsync($"Thanks for adding Niles. Type anything to get started.");

                            // save conversation channel
                            await _notificationService.StartChannel(turnContext);
                        }
                    }
                }
            }

            await _conversationState.SaveChangesAsync(turnContext);
            await _userState.SaveChangesAsync(turnContext);
        }

        // Determine if an interruption has occured before we dispatch to any active dialog.
        private async Task<bool> IsTurnInterruptedAsync(DialogContext dc, string topIntent)
        {
            // See if there are any conversation interrupts we need to handle.
            if (topIntent.Equals(CancelIntent))
            {
                if (dc.ActiveDialog != null)
                {
                    await dc.CancelAllDialogsAsync();
                    await dc.Context.SendActivityAsync("Ok. I've cancelled our last activity.");
                }
                else
                {
                    await dc.Context.SendActivityAsync("I don't have anything to cancel.");
                }

                return true;        // Handled the interrupt.
            }

            if (topIntent.Equals(HelpIntent))
            {
                await DisplayHelp(dc.Context);

                if (dc.ActiveDialog != null)
                {
                    await dc.RepromptDialogAsync();
                }

                return true;        // Handled the interrupt.
            }

            if (dc.Context.Activity.From.Id.ToLower().Equals("probot"))
            {
                string message = dc.Context.Activity.Text;

                await dc.Context.SendActivityAsync($"Thanks for the update probot \r\n{message}");

                // TODO: Handle probot notification post to Teams
                await _notificationService.NotifyChannels(dc.Context, AppId, message);
            }

            return false;           // Did not handle the interrupt.
        }

        private async Task DisplayHelp(ITurnContext ctx)
        {
            var helpCard = CreateAdaptiveCardAttachment(@".\Dialogs\Help\Resources\helpCard.json");
            var response = CreateResponse(ctx.Activity, helpCard);
            await ctx.SendActivityAsync(response).ConfigureAwait(false);

            var reply = ctx.Activity.CreateReply();
            reply.SuggestedActions = new SuggestedActions()
            {
                Actions = new List<CardAction>()
                                {
                                    new CardAction() { Title = "Create new issue", Type = ActionTypes.ImBack, Value = "Create new issue" },
                                    new CardAction() { Title = "Has my build completed", Type = ActionTypes.ImBack, Value = "Has my build completed" },
                                },
            };
            await ctx.SendActivityAsync(reply);
        }

        // Create an attachment message response.
        private Activity CreateResponse(Activity activity, Attachment attachment)
        {
            var response = activity.CreateReply();
            response.Attachments = new List<Attachment>() { attachment };
            return response;
        }

        // Load attachment from file.
        private Attachment CreateAdaptiveCardAttachment(string cardPath)
        {
            var adaptiveCard = File.ReadAllText(cardPath);
            return new Attachment()
            {
                ContentType = "application/vnd.microsoft.card.adaptive",
                Content = JsonConvert.DeserializeObject(adaptiveCard),
            };
        }

        /// <summary>
        /// Helper function to update greeting state with entities returned by LUIS.
        /// </summary>
        /// <param name="luisResult">LUIS recognizer <see cref="RecognizerResult"/>.</param>
        /// <param name="turnContext">A <see cref="ITurnContext"/> containing all the data needed
        /// for processing this conversation turn.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        private async Task UpdateGreetingState(RecognizerResult luisResult, ITurnContext turnContext)
        {
            if (luisResult.Entities != null && luisResult.Entities.HasValues)
            {
                // Get latest GreetingState
                var greetingState = await _greetingStateAccessor.GetAsync(turnContext, () => new GreetingState());
                var entities = luisResult.Entities;

                // Supported LUIS Entities
                string[] userNameEntities = { "userName", "userName_paternAny" };
                string[] userLocationEntities = { "userLocation", "userLocation_patternAny" };

                // Update any entities
                // Note: Consider a confirm dialog, instead of just updating.
                foreach (var name in userNameEntities)
                {
                    // Check if we found valid slot values in entities returned from LUIS.
                    if (entities[name] != null)
                    {
                        // Capitalize and set new user name.
                        var newName = (string)entities[name][0];
                        greetingState.Name = char.ToUpper(newName[0]) + newName.Substring(1);
                        break;
                    }
                }

                foreach (var city in userLocationEntities)
                {
                    if (entities[city] != null)
                    {
                        // Captilize and set new city.
                        var newCity = (string)entities[city][0];
                        greetingState.City = char.ToUpper(newCity[0]) + newCity.Substring(1);
                        break;
                    }
                }

                // Set the new values into state.
                await _greetingStateAccessor.SetAsync(turnContext, greetingState);
            }
        }
    }
}
