﻿#I @"tools/FAKE/tools"
#r "FakeLib.dll"

open System
open System.IO
open System.Text

open Fake
open Fake.DotNetCli

//--------------------------------------------------------------------------------
// Information about the project for Nuget and Assembly info files
//--------------------------------------------------------------------------------

let product = "Akka.NET"
let authors = [ "Akka.NET Team" ]
let copyright = "Copyright © 2013-2015 Akka.NET Team"
let company = "Akka.NET Team"
let description = "Akka.NET is a port of the popular Java/Scala framework Akka to .NET"
let tags = ["akka";"actors";"actor";"model";"Akka";"concurrency"]
let configuration = "Release"

//--------------------------------------------------------------------------------
// Directories
//--------------------------------------------------------------------------------

let slnFile = __SOURCE_DIRECTORY__ @@ "src/Akka.Persistence.MongoDb.sln"
let output = __SOURCE_DIRECTORY__  @@ "bin"
let outputTests = output @@ "TestResults"
let outputPerfTests = output @@ "perf"
let outputBinaries = output @@ "binaries"
let outputNuGet = output @@ "nuget"
let outputBinariesNet45 = outputBinaries @@ "net45"
let outputBinariesNetStandard = outputBinaries @@ "netstandard1.6"

// Read release notes and version
let buildNumber = environVarOrDefault "BUILD_NUMBER" "0"
let preReleaseVersionSuffix = (if (not (buildNumber = "0")) then (buildNumber) else "") + "-beta"
let versionSuffix = 
    match (getBuildParam "nugetprerelease") with
    | "dev" -> preReleaseVersionSuffix
    | _ -> ""

let releaseNotes =
    File.ReadLines "./RELEASE_NOTES.md"
    |> ReleaseNotesHelper.parseReleaseNotes

printfn "Assembly version: %s\nNuget version; %s\n" releaseNotes.AssemblyVersion releaseNotes.NugetVersion

//--------------------------------------------------------------------------------
// Clean build results
//--------------------------------------------------------------------------------

Target "Clean" (fun _ ->
    CleanDir output
    CleanDir outputTests
    CleanDir outputPerfTests
    CleanDir outputNuGet
    CleanDir "docs/_site"
    CleanDirs !! "./**/bin"
    CleanDirs !! "./**/obj"
)

//--------------------------------------------------------------------------------
// Restore packages
//--------------------------------------------------------------------------------

Target "RestorePackages" (fun _ ->
    DotNetCli.Restore
        (fun p -> 
            { p with
                Project = slnFile
                NoCache = false })
)

//--------------------------------------------------------------------------------
// Generate AssemblyInfo files with the version for release notes 
//--------------------------------------------------------------------------------

Target "AssemblyInfo" (fun _ ->
    XmlPokeInnerText "./src/common.props" "//Project/PropertyGroup/VersionPrefix" releaseNotes.AssemblyVersion    
    XmlPokeInnerText "./src/common.props" "//Project/PropertyGroup/PackageReleaseNotes" (releaseNotes.Notes |> String.concat "\n")
)

//--------------------------------------------------------------------------------
// Build the solution
//--------------------------------------------------------------------------------

Target "Build" (fun _ ->
    let additionalArgs = if versionSuffix.Length > 0 then [sprintf "/p:VersionSuffix=%s" versionSuffix] else []  

    let projects = !! "./**/*.csproj"

    let runSingleProject project =
        DotNetCli.Build
            (fun p -> 
                { p with
                    Project = project
                    Configuration = configuration 
                    AdditionalArgs = additionalArgs })

    projects |> Seq.iter (runSingleProject)
)

//--------------------------------------------------------------------------------
// Run tests
//--------------------------------------------------------------------------------

module internal ResultHandling =
    let (|OK|Failure|) = function
        | 0 -> OK
        | x -> Failure x

    let buildErrorMessage = function
        | OK -> None
        | Failure errorCode ->
            Some (sprintf "xUnit2 reported an error (Error Code %d)" errorCode)

    let failBuildWithMessage = function
        | DontFailBuild -> traceError
        | _ -> (fun m -> raise(FailedTestsException m))

    let failBuildIfXUnitReportedError errorLevel =
        buildErrorMessage
        >> Option.iter (failBuildWithMessage errorLevel)

Target "RunTests" <| fun _ ->
    let projects = 
        match (isWindows) with 
        | true -> !! "./src/**/*.Tests.csproj"
        | _ -> !! "./src/**/*.Tests.csproj" // if you need to filter specs for Linux vs. Windows, do it here

    ensureDirectory outputTests

    let runSingleProject project =
        let result = ExecProcess(fun info ->
            info.FileName <- "dotnet"
            info.WorkingDirectory <- (Directory.GetParent project).FullName
            info.Arguments <- (sprintf "xunit -f net452 -c Release -parallel none -teamcity -xml %s_net452_xunit.xml" (outputTests @@ fileNameWithoutExt project))) (TimeSpan.FromMinutes 30.)
        
        ResultHandling.failBuildIfXUnitReportedError TestRunnerErrorLevel.DontFailBuild result

        // dotnet process will be killed by ExecProcess (or throw if can't) '
        // but per https://github.com/xunit/xunit/issues/1338 xunit.console may not
        killProcess "xunit.console"
        killProcess "dotnet"

    projects |> Seq.iter (log)
    projects |> Seq.iter (runSingleProject)

Target "RunTestsNetCore" <| fun _ ->
    let projects = 
        match (isWindows) with 
        | true -> !! "./src/**/*.Tests.csproj"
        | _ -> !! "./src/**/*.Tests.csproj" // if you need to filter specs for Linux vs. Windows, do it here

    ensureDirectory outputTests

    let runSingleProject project =
        let result = ExecProcess(fun info ->
            info.FileName <- "dotnet"
            info.WorkingDirectory <- (Directory.GetParent project).FullName
            info.Arguments <- (sprintf "xunit -f netcoreapp1.1 -c Release -parallel none -teamcity -xml %s_netcore_xunit.xml" (outputTests @@ fileNameWithoutExt project))) (TimeSpan.FromMinutes 30.)
        
        ResultHandling.failBuildIfXUnitReportedError TestRunnerErrorLevel.DontFailBuild result

        // dotnet process will be killed by ExecProcess (or throw if can't) '
        // but per https://github.com/xunit/xunit/issues/1338 xunit.console may not
        killProcess "xunit.console"
        killProcess "dotnet"

    projects |> Seq.iter (log)
    projects |> Seq.iter (runSingleProject)

//--------------------------------------------------------------------------------
// Nuget targets 
//--------------------------------------------------------------------------------

let overrideVersionSuffix (project:string) =
    match project with
    | _ -> versionSuffix // add additional matches to publish different versions for different projects in solution

Target "CreateNuget" (fun _ ->    
    let projects = !! "src/**/*.csproj" 
                   -- "src/**/*Tests.csproj" // Don't publish unit tests
                   -- "src/**/*Tests*.csproj"

    let runSingleProject project =
        DotNetCli.Pack
            (fun p -> 
                { p with
                    Project = project
                    Configuration = configuration
                    AdditionalArgs = ["--include-symbols"]
                    VersionSuffix = overrideVersionSuffix project
                    OutputPath = outputNuGet })

    projects |> Seq.iter (runSingleProject)
)

Target "PublishNuget" (fun _ ->
    let projects = !! "./bin/nuget/*.nupkg" -- "./bin/nuget/*.symbols.nupkg"
    let apiKey = getBuildParamOrDefault "nugetkey" ""
    let source = getBuildParamOrDefault "nugetpublishurl" ""
    let symbolSource = getBuildParamOrDefault "symbolspublishurl" ""
    let shouldPublishSymbolsPackages = not (symbolSource = "")

    if (not (source = "") && not (apiKey = "") && shouldPublishSymbolsPackages) then
        let runSingleProject project =
            DotNetCli.RunCommand
                (fun p -> 
                    { p with 
                        TimeOut = TimeSpan.FromMinutes 10. })
                (sprintf "nuget push %s --api-key %s --source %s --symbol-source %s" project apiKey source symbolSource)

        projects |> Seq.iter (runSingleProject)
    else if (not (source = "") && not (apiKey = "") && not shouldPublishSymbolsPackages) then
        let runSingleProject project =
            DotNetCli.RunCommand
                (fun p -> 
                    { p with 
                        TimeOut = TimeSpan.FromMinutes 10. })
                (sprintf "nuget push %s --api-key %s --source %s" project apiKey source)

        projects |> Seq.iter (runSingleProject)
)


//--------------------------------------------------------------------------------
// Help 
//--------------------------------------------------------------------------------

Target "Help" <| fun _ ->
    List.iter printfn [
      "usage:"
      "build [target]"
      ""
      " Targets for building:"
      " * Build      Builds"
      " * Nuget      Create and optionally publish nugets packages"
      " * RunTests   Runs tests"
      " * All        Builds, run tests, creates and optionally publish nuget packages"
      ""
      " Other Targets"
      " * Help       Display this help" 
      " * HelpNuget  Display help about creating and pushing nuget packages" 
      " * HelpDocs   Display help about creating and pushing API docs" 
      ""]

Target "HelpNuget" <| fun _ ->
    List.iter printfn [
      "usage: "
      "build Nuget [nugetkey=<key> [nugetpublishurl=<url>]] "
      "            [symbolskey=<key> symbolspublishurl=<url>] "
      "            [nugetprerelease=<prefix>]"
      ""
      "Arguments for Nuget target:"
      "   nugetprerelease=<prefix>   Creates a pre-release package."
      "                              The version will be version-prefix<date>"
      "                              Example: nugetprerelease=dev =>"
      "                                       0.6.3-dev1408191917"
      ""
      "In order to publish a nuget package, keys must be specified."
      "If a key is not specified the nuget packages will only be created on disk"
      "After a build you can find them in bin/nuget"
      ""
      "For pushing nuget packages to nuget.org and symbols to symbolsource.org"
      "you need to specify nugetkey=<key>"
      "   build Nuget nugetKey=<key for nuget.org>"
      ""
      "For pushing the ordinary nuget packages to another place than nuget.org specify the url"
      "  nugetkey=<key>  nugetpublishurl=<url>  "
      ""
      "For pushing symbols packages specify:"
      "  symbolskey=<key>  symbolspublishurl=<url> "
      ""
      "Examples:"
      "  build Nuget                      Build nuget packages to the bin/nuget folder"
      ""
      "  build Nuget nugetprerelease=dev  Build pre-release nuget packages"
      ""
      "  build Nuget nugetkey=123         Build and publish to nuget.org and symbolsource.org"
      ""
      "  build Nuget nugetprerelease=dev nugetkey=123 nugetpublishurl=http://abc"
      "              symbolskey=456 symbolspublishurl=http://xyz"
      "                                   Build and publish pre-release nuget packages to http://abc"
      "                                   and symbols packages to http://xyz"
      ""]

Target "HelpDocs" <| fun _ ->
    List.iter printfn [
      "usage: "
      "build Docs"
      "Just builds the API docs for Akka.NET locally. Does not attempt to publish."
      ""
      "build PublishDocs azureKey=<key> "
      "                  azureUrl=<url> "
      "                 [unstable=true]"
      ""
      "Arguments for PublishDocs target:"
      "   azureKey=<key>             Azure blob storage key."
      "                              Used to authenticate to the storage account."
      ""
      "   azureUrl=<url>             Base URL for Azure storage container."
      "                              FAKE will automatically set container"
      "                              names based on build parameters."
      ""
      "   [unstable=true]            Indicates that we'll publish to an Azure"
      "                              container named 'unstable'. If this param"
      "                              is not present we'll publish to containers"
      "                              'stable' and the 'release.version'"
      ""
      "In order to publish documentation all of these values must be provided."
      "Examples:"
      "  build PublishDocs azureKey=1s9HSAHA+..."
      "                    azureUrl=http://fooaccount.blob.core.windows.net/docs"
      "                                   Build and publish docs to http://fooaccount.blob.core.windows.net/docs/stable"
      "                                   and http://fooaccount.blob.core.windows.net/docs/{release.version}"
      ""
      "  build PublishDocs azureKey=1s9HSAHA+..."
      "                    azureUrl=http://fooaccount.blob.core.windows.net/docs"
      "                    unstable=true"
      "                                   Build and publish docs to http://fooaccount.blob.core.windows.net/docs/unstable"
      ""]

//--------------------------------------------------------------------------------
//  Target dependencies
//--------------------------------------------------------------------------------

Target "BuildRelease" DoNothing
Target "Nuget" DoNothing

// build dependencies
"Clean" ==> "AssemblyInfo" ==> "RestorePackages" ==> "Build" ==> "BuildRelease"

// test dependencies
"RestorePackages" ==> "RunTests"
"RestorePackages" ==> "RunTestsNetCore"

// nuget dependencies
"BuildRelease" ==> "CreateNuget"
"CreateNuget" ==> "PublishNuget" ==> "Nuget"

Target "All" DoNothing
"BuildRelease" ==> "All"
"RunTests" ==> "All"
"Nuget" ==> "All"

RunTargetOrDefault "Help"