open HttpFs.Client
open FSharp.Data
open Hopac
open LibGit2Sharp
open System
open System.Diagnostics
open LibGit2Sharp.Handlers
open System.IO

type AllRepositories = JsonProvider<"""https://gist.githubusercontent.com/HigorCesar/5903c83c4f08be91a3ebab8881f5f019/raw/d9054d9c4eb029e57dda07dd1ea0e1b41b0dc99a/bitbucketInitialApiResponse.json""",InferTypesFromValues = true>
type SourceFilter = JsonProvider<"""https://gist.githubusercontent.com/HigorCesar/21c4223cf20677800cf4135cc35bdf1f/raw/a445f4ba22138ef0a00ff6303c9091823f42f3a5/sourcefilter.json""",InferTypesFromValues = true>
type NugetConfig = XmlProvider<"""https://gist.githubusercontent.com/HigorCesar/eb12cf85fcc3151b3218851259cf6668/raw/97c9c4468e20eb3625711fa5ae2a4c683f426c0f/nuget.xml""">

// Set all values below
let bitbucketUsername = "username"; //Username with privileges to all repositories
let bitbucketPassword = "password" 
let packageSourceToUpdate = "https://nuget.travix.com/nuget"
let repositoryBaseUrl = "https://higorcesar@bitbucket.org"
let newBranchName = "update-package-source"
let newPackageSourceName = "new-package-source"
let newPackageSourceUrl = "http://foo2.bar"
let author = Signature("user name","user email", DateTimeOffset.UtcNow)
//

let logsFile = "processed.csv"
[<EntryPoint>]
let main argv = 
    let doesRepoContainsNugetConfig (repository : AllRepositories.Value) = 
        Request.createUrl Get (sprintf "%s?pagelen=100" repository.Links.Source.Href)
        |> Request.responseAsString
        |> run
        |> SourceFilter.Parse
        |> (fun a -> a.Values |> Array.toList)
        |> List.exists(fun i -> String.Compare(i.Path, "nuget.config",true) = 0)

    let shouldProcess (remoteRepository : AllRepositories.Value)=
        if not(File.Exists(logsFile)) then
            true
        else
            File.ReadAllText(logsFile)
            |> String.split ','
            |> List.exists(fun (p : string) -> String.Compare(p, remoteRepository.Name,true) = 0)

    let cloneRepository (repository : AllRepositories.Value) = 
        let repoUrl = sprintf "%s/%s" repositoryBaseUrl repository.FullName
        Repository.Clone(repoUrl,(sprintf "./repositories/%s" repository.FullName)) |> (fun repoPath -> new Repository(repoPath))
    
    let checkoutNewBranch  (repository : Repository) = 
        repository.CreateBranch(newBranchName) |> ignore
        Commands.Checkout(repository, newBranchName) |> ignore
        repository;
    
    let stage repository =
        Commands.Stage(repository,"*")
        repository
    
    let commit (repository : Repository) =
        repository.Commit("add new myget package source",author,author) |> ignore
        repository
    
    let pushBranch (repository : Repository) =
        let options = new LibGit2Sharp.PushOptions();
        let userPassword = UsernamePasswordCredentials(Username = bitbucketUsername,Password = bitbucketPassword) :> Credentials
        options.CredentialsProvider <- new CredentialsHandler(fun _ _ _ -> userPassword)
        let remote = repository.Network.Remotes.["origin"]
        let action1 = new Action<BranchUpdater>(fun bu -> bu.Remote <- remote.Name)
        let action2 =  new Action<BranchUpdater>(fun bu -> bu.UpstreamBranch <- repository.Branches.[newBranchName].CanonicalName) 
        repository.Branches.Update(repository.Branches.[newBranchName], action1, action2) |> ignore
        repository.Network.Push(repository.Branches.[newBranchName], options)
        repository
    
    let dispose (repository : Repository) =
        let repoPath = repository.Info.WorkingDirectory
        repository.Dispose();
        Directory.Delete(repoPath,true)
    
    let log (remoteRepository : AllRepositories.Value) (repository : Repository)  =
        if not(File.Exists(logsFile)) then
            File.Create(logsFile) |> ignore
            repository
        else
            let processedProjects = File.ReadAllText(logsFile) |> String.split ','
            let newProcessesProjects = remoteRepository.Name :: processedProjects
            File.WriteAllText(logsFile, newProcessesProjects |> List.reduce(fun x y -> x + "," + y))
            repository

    let addNewPackageSource (localRepository : Repository) =  
        let addSourceArgs = sprintf "sources Add -Name %s -Source %s -configfile %s" newPackageSourceName newPackageSourceUrl (sprintf "%snuget.config" localRepository.Info.WorkingDirectory)
        let startInfo = new ProcessStartInfo(FileName = "../../../packages/NuGet.CommandLine.4.3.0/tools/NuGet.exe", Arguments = addSourceArgs)
        startInfo.UseShellExecute <-false
        let addPackageSource = Process.Start(startInfo)
        addPackageSource.WaitForExit(10000) |> ignore
        localRepository

    let removePackageSource (localRepository : Repository) packageSource =  
        let commandArgs = sprintf "sources Remove -Name %s -configfile %s" packageSource (sprintf "%snuget.config" localRepository.Info.WorkingDirectory)
        let startInfo = new ProcessStartInfo(FileName = "../../../packages/NuGet.CommandLine.4.3.0/tools/NuGet.exe", Arguments = commandArgs)
        startInfo.UseShellExecute <-false
        let removePackageSource = Process.Start(startInfo)
        removePackageSource.WaitForExit(10000) |> ignore
        localRepository

    let removeOldPackageSource (localRepository : Repository) =  
        let nugetConfigPath = sprintf "%snuget.config" localRepository.Info.WorkingDirectory
        let oldPackageSource = 
            System.IO.File.ReadAllText nugetConfigPath
            |> NugetConfig.Parse
            |> (fun (v : NugetConfig.Configuration) -> v.PackageSources.Adds)
            |> Array.filter(fun p -> String.Compare(p.Value, packageSourceToUpdate,true) = 0)
            |> Array.tryHead

        match oldPackageSource with
        | Some sourceToRemove -> removePackageSource localRepository sourceToRemove.Key
        | None -> localRepository

    let updatePackageSource (remoteRepository : AllRepositories.Value) = 
        remoteRepository
        |> cloneRepository
        |> checkoutNewBranch
        |> removeOldPackageSource
        |> addNewPackageSource
        |> stage
        |> commit
        |> log remoteRepository
        |> pushBranch
        |> dispose

    let UpdateAllDotNetReposWithNewPackageSource() =
        Request.createUrl Get "https://api.bitbucket.org/2.0/repositories/higorcesar"
        |> Request.responseAsString
        |> run
        |> AllRepositories.Parse
        |> (fun a -> a.Values |> Array.toList)
        |> List.filter shouldProcess
        |> List.filter doesRepoContainsNugetConfig
        |> List.take 1
        |> List.map updatePackageSource
        |> ignore

    UpdateAllDotNetReposWithNewPackageSource()
    printfn "%A" argv
    Console.ReadKey() |> ignore
    0 // return an integer exit code