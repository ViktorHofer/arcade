using System.Collections.Generic;
using Microsoft.DotNet.Darc;

/*
 Placeholder class used for testing purposes during prototyping. Darc CLI will be implemented here.
*/

namespace Darc
{
    class Program
    {
        static void Main(string[] args)
        {
            DarcSettings settings = new DarcSettings
            {
                PersonalAccessToken = "token",
            };

            DarcLib darc = new DarcLib(settings);
            DependencyItem dependencyItem = darc.RemoteAction.GetLatestDependencyAsync("arcade.*").Result;
            IEnumerable<DependencyItem> dependantItems = darc.RemoteAction.GetDependantAssetsAsync("Dependency*", type: DependencyType.Product).Result;
            IEnumerable<DependencyItem> dependencyItems = darc.RemoteAction.GetDependencyAssetsAsync("*.sd*").Result;
            IEnumerable<DependencyItem> dependenciesToUpdate = darc.RemoteAction.GetRequiredUpdatesAsync("https://github.com/dotnet/arcade/", "juanam/test").Result;
            string prLink = darc.RemoteAction.CreatePullRequestAsync(dependenciesToUpdate, "https://github.com/dotnet/arcade/", "juanam/test").Result;
            //string prLink = darc.RemoteAction.UpdatePullRequestAsync(dependenciesToUpdate, "https://github.com/jcagme/arcade/", "test", 7).Result;
        }
    }
}
