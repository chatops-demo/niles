using System;
using System.Linq;
using System.Threading.Tasks;
using BasicBot.Jobs;
using Microsoft.Bot.Builder;

namespace BasicBot.Services
{
    public class JobService
    {
        private readonly IStatePropertyAccessor<JobLog> _jobLogPropertyAccessor;
        private JobState _jobState;

        public JobService(JobState jobState)
        {
            _jobState = jobState ?? throw new ArgumentNullException(nameof(jobState));
            _jobLogPropertyAccessor = _jobState.CreateProperty<JobLog>(nameof(JobLog));
        }

        public async Task ListJobs(ITurnContext turnContext) {
            // Get the job log.
            JobLog jobLog = await GetJobLog(turnContext);

            // Display information for all jobs in the log.
            if (jobLog.Count > 0)
            {
                await turnContext.SendActivityAsync(
                    "| Job number &nbsp; | Conversation ID &nbsp; | Completed |<br>" +
                    "| :--- | :---: | :---: |<br>" +
                    string.Join("<br>", jobLog.Values.Select(j =>
                        $"| {j.TimeStamp} &nbsp; | {j.Conversation.Conversation.Id.Split('|')[0]} &nbsp; | {j.Completed} |")));
            }
            else
            {
                await turnContext.SendActivityAsync("The job log is empty.");
            }
        }

        public async Task<string> StartJob(ITurnContext turnContext)
        {
            // Get the job log.
            JobLog jobLog = await GetJobLog(turnContext);

            // Start a virtual job for the user.
            JobLog.JobData job = CreateJob(turnContext, jobLog);

            // Set the new property
            await _jobLogPropertyAccessor.SetAsync(turnContext, jobLog);

            // Now save it into the JobState
            await _jobState.SaveChangesAsync(turnContext);

            await turnContext.SendActivityAsync(
                            $"We're starting job {job.TimeStamp} for you. We'll notify you when it's complete.");

            return job.Conversation.Conversation.Id;
        }

        // Creates and "starts" a new job.
        private JobLog.JobData CreateJob(ITurnContext turnContext, JobLog jobLog)
        {
            JobLog.JobData jobInfo = new JobLog.JobData
            {
                TimeStamp = DateTime.Now.ToBinary(),
                Conversation = turnContext.Activity.GetConversationReference(),
            };

            jobLog[jobInfo.TimeStamp] = jobInfo;

            return jobInfo;
        }

        private async Task<JobLog> GetJobLog(ITurnContext turnContext) {
            // Get the job log.
            // The job log is a dictionary of all outstanding jobs in the system.
            JobLog jobLog = await _jobLogPropertyAccessor.GetAsync(turnContext, () => new JobLog());

            return jobLog;
        }
    }
}
