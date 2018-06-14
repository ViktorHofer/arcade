using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;

namespace Microsoft.DotNet.Darc
{
    public class DependencyFileContentContainer
    {
        public DependencyFileContent VersionDetailsXml { get; set; }

        public DependencyFileContent VersionProps { get; set; }

        public DependencyFileContent GlobalJson { get; set; }

        public Dictionary<string, GitCommit> GetFilesToCommitMap(string branch, string message = null)
        {
            Dictionary<string, GitCommit> gitHubCommitsMap = new Dictionary<string, GitCommit>
            {
                { VersionDetailsXml.FilePath, VersionDetailsXml.ToCommit(branch, message) },
                { VersionProps.FilePath, VersionProps.ToCommit(branch, message) },
                { GlobalJson.FilePath, GlobalJson.ToCommit(branch, message) }
            };

            return gitHubCommitsMap;
        }
    }

    public class DependencyFileContent
    {
        public string FilePath { get; }

        public string Content { get; set; }

        public DependencyFileContent(string filePath, XmlDocument xmlDocument)
            : this(filePath, GetIndentedXmlBody(xmlDocument))
        {
        }

        public DependencyFileContent(string filePath, JObject jsonObject)
            : this(filePath, jsonObject.ToString(Newtonsoft.Json.Formatting.Indented))
        {
        }

        public DependencyFileContent(string filePath, string content)
        {
            FilePath = filePath;
            Content = content;
        }

        public void Encode()
        {
            byte[] content = System.Text.Encoding.UTF8.GetBytes(Content);
            Content = Convert.ToBase64String(content);
        }

        public GitCommit ToCommit(string branch, string message = null)
        {
            message = message ?? $"Darc update of '{FilePath}'";
            Encode();
            GitCommit commit = new GitCommit(message, Content, branch);
            return commit;
        }

        private static string GetIndentedXmlBody(XmlDocument xmlDocument)
        {
            XDocument doc = XDocument.Parse(xmlDocument.OuterXml);
            return $"{doc.Declaration}\n{doc.ToString()}";
        }
    }
}
