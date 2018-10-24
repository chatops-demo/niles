using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BasicBot.Notifications;
using Microsoft.Bot.Builder;

namespace BasicBot.Services
{
    public class NotificationService
    {
        private readonly IStatePropertyAccessor<ChannelLog> _channelLogPropertyAccessor;
        private ChannelState _channelState;

        public NotificationService(ChannelState channelState)
        {
            _channelState = channelState ?? throw new ArgumentNullException(nameof(channelState));
            _channelLogPropertyAccessor = _channelState.CreateProperty<ChannelLog>(nameof(ChannelLog));
        }

        public async Task ListChannels(ITurnContext turnContext) {
            // Get the channel log.
            ChannelLog channelLog = await GetChannelLog(turnContext);

            // Display information for all channels in the log.
            if (channelLog.Count > 0)
            {
                await turnContext.SendActivityAsync(
                    "| Channel number &nbsp; | Conversation ID &nbsp; | Completed |<br>" +
                    "| :--- | :---: | :---: |<br>" +
                    string.Join("<br>", channelLog.Values.Select(c =>
                        $"| {c.TimeStamp} &nbsp; | {c.Conversation.Conversation.Id.Split('|')[0]} &nbsp; | {c.Completed} |")));
            }
            else
            {
                await turnContext.SendActivityAsync("The channel log is empty.");
            }
        }

        public async Task NotifyChannels(ITurnContext turnContext, string appId)
        {
            // Get the channel log.
            ChannelLog channelLog = await GetChannelLog(turnContext);

            var channels = channelLog.Values.ToList();

            foreach (var channel in channels)
            {
                await CompleteNotificationAsync(turnContext.Adapter, appId, channel);
            }
        }

        public async Task<string> StartChannel(ITurnContext turnContext)
        {
            // Get the channel log.
            ChannelLog channelLog = await GetChannelLog(turnContext);

            // Create channel
            ChannelLog.ChannelData channel = CreateChannel(turnContext, channelLog);

            // Set the new property
            await _channelLogPropertyAccessor.SetAsync(turnContext, channelLog);

            // Now save it into the ChannelState
            await _channelState.SaveChangesAsync(turnContext);

            await turnContext.SendActivityAsync(
                            $"We're saving {channel.Conversation.Conversation.Id} for future updates.");

            return channel.Conversation.Conversation.Id;
        }

        // Creates and saves channel info
        private ChannelLog.ChannelData CreateChannel(ITurnContext turnContext, ChannelLog channelLog)
        {
            ChannelLog.ChannelData channelInfo = new ChannelLog.ChannelData
            {
                TimeStamp = DateTime.Now.ToBinary(),
                Conversation = turnContext.Activity.GetConversationReference(),
            };

            channelLog[channelInfo.TimeStamp] = channelInfo;

            return channelInfo;
        }

        private async Task<ChannelLog> GetChannelLog(ITurnContext turnContext) {
            // Get the channel log.
            // The channel log is a dictionary of all channels in the system.
            ChannelLog channelLog = await _channelLogPropertyAccessor.GetAsync(turnContext, () => new ChannelLog());

            return channelLog;
        }

        private async Task CompleteNotificationAsync(
            BotAdapter adapter,
            string botId,
            ChannelLog.ChannelData channelInfo,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            await adapter.ContinueConversationAsync(botId, channelInfo.Conversation, CreateCallback(channelInfo), cancellationToken);
        }

        private BotCallbackHandler CreateCallback(ChannelLog.ChannelData channelInfo)
        {
            return async (turnContext, token) =>
            {
                // Get the job log from state, and retrieve the job.
                ChannelLog channelLog = await GetChannelLog(turnContext);

                // Perform bookkeeping.
                //channelLog[channelInfo.TimeStamp].Completed = true;

                // Set the new property
                //await _channelLogPropertyAccessor.SetAsync(turnContext, channelLog);

                // Now save it into the JobState
                //await _channelState.SaveChangesAsync(turnContext);

                // Send the user a proactive confirmation message.
                await turnContext.SendActivityAsync($"Notification {channelInfo.TimeStamp} is complete.");
            };
        }
    }
}
