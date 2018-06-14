namespace Microsoft.DotNet.Darc
{
    public class GitCommit
    {
        public GitCommit(string content) : this(null, content, null)
        {
        }

        public GitCommit(string message, string content, string branch)
        {
            Message = message;
            Content = content;
            Branch = branch;
        }

        public string Message { get; set; }

        public string Content { get; set; }

        public string Branch { get; set; }

        public string Sha { get; set; }
    }
}
