using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

/*
 Prototype class. We'll need to: 
    *  Replace KeyVaultManager with the logic we currently use in Helix to get stuff off of KV
    *  Instead of using Console to write the output use an ILogger interface
    *  Probably instead of having queries here, add APIs to Helix or some other place which uses a token level auth
     */
namespace Microsoft.DotNet.Darc
{
    public class RemoteActions : IRemote
    {
        private readonly DependencyFileManager fileManager;

        private readonly IGitRepo gitClient;

        public RemoteActions(DarcSettings settings)
        {
            ValidateSettings(settings);

            if (settings.GitType == GitRepoType.GitHub)
            {
                gitClient = new GitHubClient(settings.PersonalAccessToken);
            }
            else
            {
                gitClient = new VstsClient(settings.PersonalAccessToken);
            }

            fileManager = new DependencyFileManager(gitClient);
        }

        public async void Test()
        {
            await gitClient.CreateDarcBranchAsync("https://devdiv.visualstudio.com/DefaultCollection/DevDiv/_git/chcosta-explore", "master");
        }

        public async Task<IEnumerable<DependencyItem>> GetDependantAssetsAsync(string assetName, string version = null, string repoUri = null, string branch = null, string sha = null, DependencyType type = DependencyType.Unknown)
        {
            return await GetAssetsAsync(assetName, RelationType.Dependant, "Getting assets which depend on", version, repoUri, branch, sha, type);
        }

        public async Task<IEnumerable<DependencyItem>> GetDependencyAssetsAsync(string assetName, string version = null, string repoUri = null, string branch = null, string sha = null, DependencyType type = DependencyType.Unknown)
        {
            return await GetAssetsAsync(assetName, RelationType.Dependency, "Getting dependencies of", version, repoUri, branch, sha, type);
        }

        public async Task<DependencyItem> GetLatestDependencyAsync(string assetName)
        {
            List<DependencyItem> dependencies = new List<DependencyItem>();

            Console.WriteLine($"Getting latest dependency version for '{assetName}' in the reporting store...");

            assetName = assetName.Replace('*', '%').Replace('?', '%');

            using (SqlConnection connection = new SqlConnection(await KeyVaultManager.GetSecretAsync("LogAnalysisWriteConnectionString")))
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = $@"
SELECT TOP 1 [AssetName]
    ,[Version]
    ,[RepoUri]
    ,[Branch]
    ,[Sha]
    ,[Type] 
FROM AssetDependency
WHERE AssetName like '{assetName}' AND GitType = '{gitClient.Type}'
ORDER BY DateProduced DESC";
                await connection.OpenAsync();

                SqlDataReader reader = await command.ExecuteReaderAsync();

                dependencies = await BuildDependencyItemCollectionAsync(reader);
            }

            if (!dependencies.Any())
            {
                Console.WriteLine($"No dependencies were found matching {assetName}.");
            }
            else
            {
                Console.WriteLine($"Getting latest dependency version for '{assetName}' in the reporting store succeeded!");
            }

            return dependencies.FirstOrDefault();
        }

        public async Task<IEnumerable<DependencyItem>> GetRequiredUpdatesAsync(string repoUri, string branch)
        {
            Console.WriteLine($"Getting dependencies which need to be updated in repo '{repoUri}' and branch '{branch}'...");

            List<DependencyItem> toUpdate = new List<DependencyItem>();
            IEnumerable<DependencyItem> dependencies = await fileManager.ParseVersionDetailsXmlAsync(repoUri, branch);

            foreach (DependencyItem dependencyItem in dependencies)
            {
                DependencyItem latest = await GetLatestDependencyAsync(dependencyItem.Name);

                if (latest != null)
                {
                    if (string.Compare(latest.Version, dependencyItem.Version) == 1)
                    {
                        dependencyItem.Version = latest.Version;
                        dependencyItem.Sha = latest.Sha;
                        toUpdate.Add(dependencyItem);
                    }
                }
                else
                {
                    Console.WriteLine($"No asset with name '{dependencyItem.Name}' found in store but it is defined in repo '{repoUri}' and branch '{branch}'.");
                }
            }

            Console.WriteLine($"Getting dependencies which need to be updated in repo '{repoUri}' and branch '{branch}' succeeded!");

            return toUpdate;
        }

        public async Task<string> CreatePullRequestAsync(IEnumerable<DependencyItem> itemsToUpdate, string repoUri, string branch, string pullRequestBaseBranch = null, string pullRequestTitle = null, string pullRequestDescription = null)
        {
            Console.WriteLine($"Create pull request to update dependencies in repo '{repoUri}' and branch '{branch}'...");
            string linkToPr = null;
            await gitClient.CreateDarcBranchAsync(repoUri, branch);
            pullRequestBaseBranch = pullRequestBaseBranch ?? $"darc-{branch}";

            // Check for exsting PRs in the darc created branch. If there is one under the same user we fail fast before commiting files that won't be included in a PR. 
            string existingPr = await gitClient.CheckForOpenedPullRequestsAsync(repoUri, pullRequestBaseBranch);

            if (string.IsNullOrEmpty(existingPr))
            {
                DependencyFileContentContainer fileContainer = await fileManager.UpdateDependencyFiles(itemsToUpdate, repoUri, branch);
                await gitClient.PushFilesAsync(fileContainer.GetFilesToCommitMap(pullRequestBaseBranch), repoUri, pullRequestBaseBranch);

                // If there is an arcade asset that we need to update we try to update the script files as well
                DependencyItem arcadeItem = itemsToUpdate.Where(i => i.Name.Contains("arcade")).FirstOrDefault();
                if (arcadeItem != null)
                {
                    await gitClient.PushFilesAsync(await GetScriptCommitsAsync(pullRequestBaseBranch, assetName: arcadeItem.Name), repoUri, pullRequestBaseBranch);
                    linkToPr = await gitClient.CreatePullRequestAsync(repoUri, branch, pullRequestBaseBranch, pullRequestTitle, pullRequestDescription);
                    Console.WriteLine($"Updating dependencies in repo '{repoUri}' and branch '{branch}' succeeded! PR link is: {linkToPr}");
                    return linkToPr;
                }
            }

            Console.WriteLine($"PR with link '{existingPr}' is already opened in repo '{repoUri}' and branch '{pullRequestBaseBranch}' please update it instead of trying to create a new one");
            return linkToPr;
        }

        public async Task<string> UpdatePullRequestAsync(IEnumerable<DependencyItem> itemsToUpdate, string repoUri, string branch, int pullRequestId, string pullRequestBaseBranch = null, string pullRequestTitle = null, string pullRequestDescription = null)
        {
            Console.WriteLine($"Updating pull request '{pullRequestId}' in repo '{repoUri}' and branch '{branch}'...");
            string linkToPr = null;

            pullRequestBaseBranch = pullRequestBaseBranch ?? $"darc-{branch}";

            DependencyFileContentContainer fileContainer = await fileManager.UpdateDependencyFiles(itemsToUpdate, repoUri, branch);
            await gitClient.PushFilesAsync(fileContainer.GetFilesToCommitMap(pullRequestBaseBranch), repoUri, pullRequestBaseBranch);
            
            // If there is an arcade asset that we need to update we try to update the script files as well
            DependencyItem arcadeItem = itemsToUpdate.Where(i => i.Name.Contains("arcade")).FirstOrDefault();
            if (arcadeItem != null)
            {
                await gitClient.PushFilesAsync(await GetScriptCommitsAsync(branch, assetName: arcadeItem.Name), repoUri, pullRequestBaseBranch);
                linkToPr = await gitClient.UpdatePullRequestAsync(repoUri, branch, pullRequestBaseBranch, pullRequestId, pullRequestTitle, pullRequestDescription);
                Console.WriteLine($"Updating dependencies in repo '{repoUri}' and branch '{branch}' succeeded! PR link is: {linkToPr}");
            }

            return linkToPr;
        }

        private void ValidateSettings(DarcSettings settings)
        {
            if (string.IsNullOrEmpty(settings.PersonalAccessToken))
            {
                throw new ArgumentException("When using remote actions a personal access token has to be set.");
            }
        }

        private async Task<Dictionary<string, GitCommit>> GetScriptCommitsAsync(string branch, string assetName = "arcade.sdk")
        {
            Console.WriteLine($"Generating commits for script files");
            DependencyItem latestAsset = await GetLatestDependencyAsync(assetName);
            Dictionary<string, GitCommit> commits = await gitClient.GetCommitsForPathAsync(latestAsset.RepoUri, latestAsset.Sha, branch);
            Console.WriteLine($"Generating commits for script files succeeded!");
            return commits;
        }

        private async Task<IEnumerable<DependencyItem>> GetAssetsAsync(string assetName, RelationType relationType, string logMessage, string version = null, string repoUri = null, string branch = null, string sha = null, DependencyType type = DependencyType.Unknown)
        {
            string conditionPrefix = relationType == RelationType.Dependant ? "Dependency" : null;
            string selectPrefix = conditionPrefix == null ? "Dependency" : null;
            List<DependencyItem> dependencies = new List<DependencyItem>();

            QueryParameter queryParameters = CreateQueryParameters(assetName, version, repoUri, branch, sha, type, conditionPrefix);

            Console.WriteLine($"{logMessage} {queryParameters.loggingConditions}...");

            using (SqlConnection connection = new SqlConnection(await KeyVaultManager.GetSecretAsync("LogAnalysisWriteConnectionString")))
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = $@"
SELECT DISTINCT [{selectPrefix}AssetName]
    ,[{selectPrefix}Version]
    ,[{selectPrefix}RepoUri]
    ,[{selectPrefix}Branch]
    ,[{selectPrefix}Sha]
    ,[{selectPrefix}Type] 
FROM AssetDependency
WHERE {queryParameters.whereConditions} AND GitType = '{gitClient.Type}'";
                await connection.OpenAsync();

                SqlDataReader reader = await command.ExecuteReaderAsync();
                dependencies = await BuildDependencyItemCollectionAsync(reader);

                if (!dependencies.Any())
                {
                    Console.WriteLine($"No dependencies were found matching {queryParameters.loggingConditions}.");
                }
            }

            return dependencies;
        }

        private async Task<List<DependencyItem>> BuildDependencyItemCollectionAsync(SqlDataReader reader)
        {
            List<DependencyItem> dependencies = new List<DependencyItem>();

            if (reader.HasRows)
            {
                while (await reader.ReadAsync())
                {
                    DependencyType dependencyType = (DependencyType)Enum.Parse(typeof(DependencyType), reader.GetString(5));
                    if (!Enum.IsDefined(typeof(DependencyType), dependencyType))
                    {
                        Console.WriteLine($"DependencyType {dependencyType} in not defined in the DependencyType enum. Defaulting to {DependencyType.Unknown}");
                        dependencyType = DependencyType.Unknown;
                    }

                    DependencyItem dependencyItem = new DependencyItem
                    {
                        Name = reader.GetString(0),
                        Version = reader.GetString(1),
                        RepoUri = reader.GetString(2),
                        Branch = reader.GetString(3),
                        Sha = reader.GetString(4),
                        Type = dependencyType,
                    };

                    dependencies.Add(dependencyItem);
                }
            }

            return dependencies;
        }

        private QueryParameter CreateQueryParameters(string assetName, string version, string repoUri, string branch, string sha, DependencyType type, string prefix = null)
        {
            QueryParameter queryParameters = new QueryParameter();
            queryParameters.loggingConditions.Append($"{prefix}AssetName = '{assetName}'");
            assetName = assetName.Replace('*', '%').Replace('?', '%');
            queryParameters.whereConditions.Append($"{prefix}AssetName like '{assetName}'");

            if (version != null)
            {
                queryParameters.loggingConditions.Append($", {prefix}Version = '{version}'");
                version = version.Replace('*', '%').Replace('?', '%');
                queryParameters.whereConditions.Append($"AND {prefix}Version like '{version}'");
            }

            if (repoUri != null)
            {
                queryParameters.loggingConditions.Append($", {prefix}RepoUri = '{repoUri}'");
                repoUri = repoUri.Replace('*', '%').Replace('?', '%');
                queryParameters.whereConditions.Append($"AND {prefix}RepoUri like '{repoUri}'");
            }

            if (branch != null)
            {
                queryParameters.loggingConditions.Append($", {prefix}Branch = '{branch}'");
                branch = branch.Replace('*', '%').Replace('?', '%');
                queryParameters.whereConditions.Append($"AND {prefix}Branch like '{branch}'");
            }

            if (sha != null)
            {
                queryParameters.loggingConditions.Append($", {prefix}Sha = '{sha}'");
                queryParameters.whereConditions.Append($"AND {prefix}Sha = '{sha}'");
            }

            if (type != DependencyType.Unknown)
            {
                queryParameters.loggingConditions.Append($", {prefix}Type = '{type}'");
                queryParameters.whereConditions.Append($"AND {prefix}Type = '{type}'");
            }

            return queryParameters;
        }
    }
}
