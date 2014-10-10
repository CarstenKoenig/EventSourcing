// include Fake lib
#r @"packages\FAKE\tools\FakeLib.dll"
open Fake

RestorePackages()

let buildDir = "./build"
let testDir  = "./test"
let testDlls = !! (testDir + "/*.Tests.dll")

// Targets
Target "Clean" (fun _ ->
    CleanDirs [buildDir; testDir])

Target "CoreLib" (fun _ ->
    !! "src/EventSourcing/*.fsproj"
        |> MSBuildRelease buildDir "Build"
        |> Log "Core Build - Output:")

Target "Repository" (fun _ ->
    !! "src/EventSourcing.EntityFramework/*.fsproj"
        |> MSBuildRelease buildDir "Build"
        |> Log "Repository Build - Output:")

Target "Tests" (fun _ ->
    !! "src/EventSourcing.Tests/*.fsproj"
        |> MSBuildRelease testDir "Build"
        |> Log "Test Build - Output:")

Target "RunTests" (fun _ ->
    testDlls
        |> xUnit (fun p -> 
            {p with 
                ToolPath   = "./tools/xUnit/xunit.console.exe"
                ConfigFile = "./tools/xUnit/xunit.console.exe.config"
                OutputDir = testDir }))

Target "Default" (fun _ ->
    trace "all done"
)

// Dependencies
"Clean"
    ==> "CoreLib"
    ==> "Repository"
    ==> "Tests"
    ==> "RunTests"
    ==> "Default"

// start build
RunTargetOrDefault "Default"