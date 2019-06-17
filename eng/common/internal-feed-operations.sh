#!/usr/bin/env bash

function SetupCredProvider {
  local authToken=$1
  
  # Install the Cred Provider NuGet plugin
  echo "Setting up Cred Provider NuGet plugin in the agent..."...
  echo "Getting 'installcredprovider.ps1' from 'https://github.com/microsoft/artifacts-credprovider'..."

  local url="https://raw.githubusercontent.com/microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh"  
  
  echo "Writing the contents of 'installcredprovider.ps1' locally..."
  local installcredproviderPath="installcredprovider.sh"
  if command -v curl > /dev/null; then
    curl $url > "$installcredproviderPath"
  else   
    wget -q -O "$installcredproviderPath" "$url"
  fi
  
  echo "Installing plugin..."
  . "$installcredproviderPath"
  
  echo "Deleting local copy of 'installcredprovider.sh'..."
  rm installcredprovider.sh

  if [ ! -d "$HOME/.nuget/plugins" ]; then
    echo "CredProvider plugin was not installed correctly!"
    ExitWithExitCode 1  
  else 
    echo "CredProvider plugin was installed correctly!"
  fi

  # Then, we set the 'VSS_NUGET_EXTERNAL_FEED_ENDPOINTS' environment variable to restore from the stable 
  # feeds successfully

  local nugetConfigPath="$repo_root/NuGet.config"

  if [ ! "$nugetConfigPath" ]; then
    echo "NuGet.config file not found in repo's root!"
    ExitWithExitCode 1  
  fi
  
  local endpoints='['
  local nugetConfigPackageValues=`cat "$nugetConfigPath" | grep "key=\"darc-"`
  local pattern="value=\"(.*)\""

  for value in $nugetConfigPackageValues 
  do
    if [[ $value =~ $pattern ]]; then
      local endpoint="${BASH_REMATCH[1]}"  
      endpoints+="{\"endpoint\": \"$endpoint\", \"password\": \"$authToken\"},"
    fi
  done
  
  endpoints=${endpoints%?}
  endpoints+=']'

  if [ ${#endpoints} -gt 2 ]; then 
      # Create the JSON object. It should look like '{"endpointCredentials": [{"endpoint":"http://example.index.json", "username":"optional", "password":"accesstoken"}]}'
      local endpointCredentials="{\"endpointCredentials\": "$endpoints"}"
      local restoreProjPath="$repo_root/eng/common/restore.proj"

      echo "##vso[task.setvariable variable=VSS_NUGET_EXTERNAL_FEED_ENDPOINTS]$endpointCredentials"
      echo "##vso[task.setvariable variable=NUGET_CREDENTIALPROVIDER_SESSIONTOKENCACHE_ENABLED]False"

      echo "<Project Sdk=\"Microsoft.DotNet.Arcade.Sdk\"/>" > "$restoreProjPath"
  else
    echo "No internal endpoints found in NuGet.config"
  fi
} 

# Workaround for https://github.com/microsoft/msbuild/issues/4430
function InstallDotNetSdkAndRestoreArcade {
  local dotnetTempDir="$repo_root/dotnet"
  local dotnetSdkVersion="2.1.507"
  echo "Installing dotnet SDK version $dotnetSdkVersion to restore Arcade SDK..."
  InstallDotNetSdk "$dotnetTempDir" "$dotnetSdkVersion"

  local res=`$dotnetTempDir/dotnet restore $restoreProjPath`
  echo "Arcade SDK restored!"

  # Cleanup
  if [ "$restoreProjPath" ]; then
    rm "$restoreProjPath"
  fi

  if [ "$dotnetTempDir" ]; then
    rm -r $dotnetTempDir
  fi
}

source="${BASH_SOURCE[0]}"
operation=''
authToken=''
repoName=''

while [[ $# > 0 ]]; do
  opt="$(echo "$1" | awk '{print tolower($0)}')"
  case "$opt" in
    --operation)
      operation=$2
      shift
      ;;
    --authtoken)
      authToken=$2
      shift
      ;;
    *)
      echo "Invalid argument: $1"
      usage
      exit 1
      ;;
  esac

  shift
done

while [[ -h "$source" ]]; do
  scriptroot="$( cd -P "$( dirname "$source" )" && pwd )"
  source="$(readlink "$source")"
  # if $source was a relative symlink, we need to resolve it relative to the path where the
  # symlink file was located
  [[ $source != /* ]] && source="$scriptroot/$source"
done
scriptroot="$( cd -P "$( dirname "$source" )" && pwd )"

. "$scriptroot/tools.sh"

if [ "$operation" = "setup" ]; then
  SetupCredProvider $authToken
elif [ "$operation" = "install-restore" ]; then
  InstallDotNetSdkAndRestoreArcade
else
  echo "Unknown operation '$operation'!"
fi