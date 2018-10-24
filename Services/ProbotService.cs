using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace BasicBot.Services
{
    public class ProbotService
    {
        private HttpClient _httpClient;

        public ProbotService()
        {
            _httpClient = new HttpClient();
        }

        public async Task PostIssue(IssueDTO issue)
        {
            var result = await _httpClient.PostAsJsonAsync("https://dmc-probot1.glitch.me/dow-dev-probot/new-issue", issue);
        }
    }

    public class IssueDTO
    {
        public string conversationId;

        public string issue;
    }
}
