using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BasicBot.Dialogs.CreateIssues
{
    public class CreateIssueState
    {
        public string RepoName { get; set; }

        public string IssueTitle { get; set; }

        public string IssueBody { get; set; }
    }
}
