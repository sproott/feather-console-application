local_nuget := "~/.nuget/local"

# Pack via build.sh and push to local NuGet feed
publish-local:
    ./build.sh -t release
    dotnet nuget push release/Feather.ConsoleApplication.*.nupkg --source {{local_nuget}}
