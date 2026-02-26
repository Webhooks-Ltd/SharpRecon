param(
    [string]$Configuration = "Release"
)

dotnet publish "$PSScriptRoot/src/SharpRecon/SharpRecon.csproj" -c $Configuration -o "$PSScriptRoot/src/SharpRecon/bin/publish"
