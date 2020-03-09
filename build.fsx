#r "packages/build/FAKE/tools/FakeLib.dll"

open Fake
open Fake.Git
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper
open System
open System.IO



// These values power the content of the nuspec file and the AssemblyInfo file
let ProjectDescription = "Code generation tool that applies expansion functions to simple types"
let CopyrightYearStart = 2016
let CopyrightInfo = sprintf "Copyright © %s amazingant (Anthony Perez)"
let ReleaseNotesFile = "RELEASE_NOTES.md"
let GitHubUser = "amazingant"
let GitHubRepo = "Amazingant.FSharp.TypeExpansion"




let buildCopyrightText (now : System.DateTime) =
    let startYear = CopyrightYearStart
    let format = CopyrightInfo
    let years =
        if now.Year > startYear then
            sprintf "%i-%i" startYear now.Year
        else
            startYear.ToString()
    CopyrightInfo years

let release = LoadReleaseNotes ReleaseNotesFile

let buildConfig =
    match release.SemVer.PreRelease with
    | None -> "Release"
    | Some _ -> "Debug"

let assemblyInfoAttributes (asmFile) =
    let projectName = FileInfo(asmFile).Directory.Name
    let copyrightYear = match release.Date with None -> System.DateTime.Now | Some dt -> dt
    [
        Attribute.Title projectName;
        Attribute.Product projectName;
        Attribute.Description ProjectDescription;
        Attribute.Copyright <| buildCopyrightText copyrightYear;
        Attribute.Version release.AssemblyVersion;
        Attribute.FileVersion release.AssemblyVersion;
        Attribute.InformationalVersion release.NugetVersion;
    ]


let doGit = Fake.Git.CommandHelper.runGitCommand __SOURCE_DIRECTORY__

// Helper for putting line-endings back to \n after the AssemblyInfo target runs
let fixLineEndings (filePath : string) =
    let oldFile = filePath + ".old"
    if File.Exists oldFile then File.Delete oldFile
    File.Move(filePath, oldFile)
    use reader = new StreamReader(oldFile)
    use writer = new StreamWriter(filePath)
    while not (reader.EndOfStream) do
        let line = reader.ReadLine()
        writer.Write(line + "\n")
    reader.Close()
    writer.Close()
    File.Delete oldFile


// -----------------------------------------------------------------------------
// Build targets start here
// -----------------------------------------------------------------------------

Target "AssemblyInfo" (fun _ ->
    !! "Source/**/AssemblyInfo.fs"
    |> Seq.iter
        (fun x ->
            let attribs = assemblyInfoAttributes x
            CreateFSharpAssemblyInfo x attribs
            fixLineEndings x
        )
)


Target "CopyBinaries" (fun _ ->
    !! "Source/**/*.??proj"
    |> Seq.iter
        (fun x ->
            let outDir = "bin" @@ (FileInfo(x).Directory.Name)
            let sourceDir = Path.Combine(FileInfo(x).DirectoryName, "bin", buildConfig)
            printfn "Copying '%s' to '%s'" sourceDir outDir
            CopyDir outDir sourceDir (fun _ -> true)
        )
)


Target "Clean" (fun _ ->
    CleanDirs [ "bin" ]
    !! "Source/**/*.??proj"
    |> Seq.iter
        (fun x ->
            MSBuild "" "Clean" [("Configuration", buildConfig)] [x] |> ignore
        )
)


Target "Build" (fun _ ->
    !! "*.sln"
    |> Seq.iter
        (fun x ->
            MSBuild "" "Rebuild" [("Configuration", buildConfig)] [x] |> ignore
        )
)


Target "Package" (fun _ ->
    !! "Source/**/*.??proj"
    |> Seq.iter
        (fun x ->
            let proj = (FileInfo(x).Directory.Name)
            // Delete the .nupkg file if it already exists
            let nupkgFile = "bin" @@ (proj + "." + release.NugetVersion + ".nupkg")
            if File.Exists nupkgFile then File.Delete nupkgFile

            Paket.Pack (fun p ->
                { p with
                    OutputPath = "bin";
                    Version = release.NugetVersion;
                    ReleaseNotes = release.Notes |> toLines;
                    BuildConfig = buildConfig;
                })
        )
)


Target "CommitAndTag" (fun _ ->
    StageAll ""
    Git.Commit.Commit __SOURCE_DIRECTORY__ (sprintf "Automated version bump to %s" release.NugetVersion)
    Branches.tag __SOURCE_DIRECTORY__ release.NugetVersion
)


// Builds everything and dumps things into the bin folder for testing
Target "Default" DoNothing
// Commits the AssemblyInfo changes to git, links PDBs, and builds a package for
// distribution
Target "Release" DoNothing


"Clean"
    ==> "AssemblyInfo"
    ==> "Build"
    ==> "CopyBinaries"
    ==> "Default"

"Build"
    ==> "Package"
    ==> "Release"


"CommitAndTag" ==> "Release"
"CopyBinaries" ==> "Package"


RunTargetOrDefault "Default"
