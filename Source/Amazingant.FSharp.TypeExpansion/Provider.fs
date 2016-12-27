namespace Amazingant.FSharp.TypeExpansion.Provider

open Amazingant.FSharp.TypeExpansion

open System
open System.Collections.Generic
open System.IO
open System.Reflection

open Microsoft.FSharp.Quotations
open Microsoft.FSharp.Core.CompilerServices


/// <summary>
/// Determines how the expanded types are presented for use
/// </summary>
type OutputMode =
    /// <summary>
    /// Acts like a generative type provider, and embeds the expanded code into
    /// the project where the provider was utilized.
    /// </summary>
    /// <remarks>
    /// Note that in this mode, the source file(s) used need to not be set to
    /// compile as part of the project, as their contents will be compiled in by
    /// the provider.
    /// </remarks>
    | BuildIntoProject = 0
    /// <summary>
    /// Creates an external assembly at a specified output location. This
    /// assembly will contain the compiled results of the specified source
    /// file(s), as well as the expanded code.
    /// </summary>
    /// <remarks>
    /// This is the safest option, but requires specifying a target library and
    /// referencing it from other projects, rather than just providing a
    /// "project reference" through Visual Studio.
    /// </remarks>
    | CreateAssembly   = 1
    /// <summary>
    /// Compiles and executes the source file(s) in the same way as the other
    /// modes, but places the expanded code into a specified source file for
    /// consumption by the host project.
    /// </summary>
    /// <remarks>
    /// When this option is used, the specified output path is skipped during
    /// compilation to avoid recursive use of this provider.
    ///
    /// Note that while this is not the safest option (as the user must remember
    /// to build the project before e.g. committing changes that will affect the
    /// expanded code), it does mean that the expanded code will be available
    /// for inclusion into source control, and will make debugging easier.
    ///
    /// Please be aware that when using this mode, the output code file must not
    /// be manually modified, as its contents can be replaced at any time that
    /// Visual Studio or the build system feels like calling this provider
    /// again.
    /// </remarks>
    | CreateSourceFile = 2

type internal StaticParameters =
    {
        SourcePath : string;
        WorkingDir : string;
        ExcludeFiles : string;
        Refs : string;
        Flags : string;
        CompilerTimeout : int;
        OutputMode : OutputMode;
        OutputPath : string;
    }
    member x.Source =
        let exc =
            if System.String.IsNullOrWhiteSpace x.ExcludeFiles
            then []
            else ((CompileSource (x.ExcludeFiles, [], x.WorkingDir)).FilesAndRefs()) |> fst
        match x.OutputMode with
        | OutputMode.CreateSourceFile ->
            CompileSource (x.SourcePath, x.OutputPath::exc, x.WorkingDir)
        | _ ->
            CompileSource (x.SourcePath, exc, x.WorkingDir)
    member x.References = splitValues x.Refs
    member x.CompilerFlags = splitValues x.Flags
    member x.SourceModifiedTimes =
        fst (x.Source.FilesAndRefs())
        |> List.map (fun x -> x, File.GetLastWriteTimeUtc x)
    member x.IsValid () =
        let missingFiles = (x.Source.FilesAndRefs()) |> fst |> Seq.filter File.NotExists |> joinLines
        // If any files are missing, throw an exception that indicates the
        // current path; this will allow the user to determine how to fix
        // any relative paths that they specified
        match missingFiles.Trim() with
        | null | "" -> ()
        | xs ->
            failwithf "Specified file(s) do not exist; paths are relative to:\n%s\nIf this does not appear right, try setting the WorkingDirectory parameter?\n\n%s\n"
                x.WorkingDir
                xs
        true

type Expand () = inherit obj()

[<TypeProvider>]
type ExpansionProvider (tpConfig : TypeProviderConfig) =
    let invalidateEvent = new Event<EventHandler, EventArgs>()
    static let state = new Dictionary<StaticParameters, (string * DateTime) list * Assembly option * Assembly>()
    static let mutable currentAssembly : StaticParameters option = None


    // Builds an IProvidedNamespace out of a namespace name and a collection of
    // types
    let makeProvidedNamespace ((namespaceName : string), (types : Type seq)) =
        let types = types |> Seq.toArray
        {
            new IProvidedNamespace with
                // This property does not seem to be used in most type
                // providers; as such, not really sure what it is used for.
                member __.GetNestedNamespaces () = [| |]
                // This should match the namespace that holds all of the types
                // contained in this instance
                member __.NamespaceName = namespaceName
                // Returns a copy of the types in this namespace
                member __.GetTypes () = types |> Array.copy
                // Returns the requested type if it exists; returns null
                // otherwise
                member __.ResolveTypeName typeName = types |> Array.tryFind (fun x -> x.Name = typeName) |> Option.toObj
        }


    let buildCoreNamespace () =
        makeProvidedNamespace
            (
                typeof<Expand>.Namespace.Replace(".Provider", ""),
                [typeof<Expand>]
            )


    let providedNamespaces () =
        currentAssembly
        |> Option.map
            (fun x ->
                state.[x]
                |> function (_,x,_) -> x
                |> Option.map
                    (fun x ->
                        x.DefinedTypes
                        |> Seq.map (fun x -> x :> Type)
                        |> Seq.groupBy (fun x -> x.Namespace)
                        |> Seq.map makeProvidedNamespace
                        |> Seq.toArray
                    )
                |> switch defaultArg [||]
            )
        |> (switch defaultArg [||])
        |> Array.append [| buildCoreNamespace() |]


    // Create a dummy assembly and compile it; this will be returned to Visual
    // Studio when it asks for the contents of the "provided" assembly
    let buildProvidedAssembly (ns, ty) (config : StaticParameters) =
        let providedCode = Path.ChangeExtension(Path.GetTempFileName(), ".dummy.fs")
        File.WriteAllText(providedCode, sprintf "namespace %s\ntype %s() =\n    member __.Dummy = true" ns ty)
        let providedPath = Path.ChangeExtension(providedCode, ".dummy.dll")
        let args = [| "--noframework"; "--target:library"; (sprintf "-o:%s" providedPath); providedCode |]
        runFsc args config.CompilerTimeout config.WorkingDir
        if File.NotExists providedPath then
            failwithf "Could not compile dummy library for type provider"
        else
            providedPath


    // Builds an assembly that contains the original source and the expanded
    // source together
    let buildFinalAssembly (config : StaticParameters) (newCode : string) =
        // Get temp file paths, then write the expanded source so it can be
        // compiled
        let tempCodePath = Path.ChangeExtension(Path.GetTempFileName(), ".fs")
        let  tempLibPath = Path.ChangeExtension(Path.GetTempFileName(), ".dll")
        File.WriteAllText(tempCodePath, newCode)
        // Base compiler flags needed
        let baseArgs = [ "--noframework"; "--target:library"; ]
        let (files, refs) = config.Source.FilesAndRefs()
        // Library references
        let refs = buildRefs (requiredRefs @ config.References @ refs)
        // Group everything together with the source files
        let args = Seq.concat [baseArgs; [sprintf "-o:%s" tempLibPath;]; refs; files; [tempCodePath]; config.CompilerFlags] |> Seq.toArray
        // Compile!
        runFsc args config.CompilerTimeout config.WorkingDir
        // Return the library file path
        if File.NotExists tempLibPath then
            failwithf "Could not compile final assembly"
        else
            tempLibPath


    let buildAssembly (config : StaticParameters) (ns, ty) =
        // Process the source files and expand appropriate types
        let newCode = processFiles config.Source config.References config.CompilerFlags config.CompilerTimeout config.WorkingDir
        // Build an assembly with some dummy info to make Visual Studio happy
        let providedAssembly = buildProvidedAssembly (ns, ty) config

        match config.OutputMode with
        // This is the only mode that returns a value for the actual assembly,
        // since it needs to be built into the host project
        | OutputMode.BuildIntoProject ->
            let asm = buildFinalAssembly config newCode
            (Some asm), providedAssembly

        // In this mode, build the actual assembly and then copy it to the
        // specified output path
        | OutputMode.CreateAssembly ->
            let asm = buildFinalAssembly config newCode
            File.Copy (asm, config.OutputPath, true)
            None, providedAssembly

        // In this mode, just dump the new code into the specified file path
        | OutputMode.CreateSourceFile ->
            let header =
                let (files, refs) = config.Source.FilesAndRefs false
                let files = files |> Seq.map (sprintf "#load @\"%s\"")
                let refs = refs |> Seq.map (sprintf "#r @\"%s\"")
                Seq.append refs files
                |> fun xs -> System.String.Join("\n", xs)
                |> sprintf """
#if INTERACTIVE
%s
#endif

(*
 * This file was auto-generated by the TypeExpansion type provider
 *
 * The contents of this file are likely to be overwritten at any time while
 * Visual Studio or F# Interactive is open, or while a build is in progress.
 *
 * Please do not make important changes to this file.
 *)

"""
            let newCode = header + newCode
            File.WriteAllText(config.OutputPath, newCode)
            None, providedAssembly

        | _ -> failwithf "Invalid OutputMode specified"

    // Applies the given static type arguments for the specified type
    let applyStaticArguments ty (y : string []) (args : obj []) =
        if ty <> typeof<Expand> then
            failwithf "This provider cannot apply static type arguments to the type '%s'" ty.FullName

        let config =
            {
                        SourcePath = args.[0] :?> string;
                        WorkingDir = args.[1] :?> string;
                      ExcludeFiles = args.[2] :?> string;
                              Refs = args.[3] :?> string;
                             Flags = args.[4] :?> string;
                   CompilerTimeout = args.[5] :?> int;
                        OutputPath = args.[6] :?> string;
                        OutputMode = args.[7] :?> OutputMode;
            }
        let wd =
            if System.String.IsNullOrWhiteSpace config.WorkingDir
            then System.Environment.CurrentDirectory
            elif not <| Path.IsPathRooted config.WorkingDir then
                failwithf "System.IO.Path.IsPathRooted indicates that the provided working directory is not an absolute path:\n%s\nDid you mean to specify e.g. '(__SOURCE_DIRECTORY__)'?"
                    config.WorkingDir
            else config.WorkingDir
        let config = { config with WorkingDir = wd }
        // For either of the two modes that make output files, check the
        // specified output path
        match config.OutputMode with
        | OutputMode.CreateAssembly ->
            if not <| config.OutputPath.EndsWith ".dll" then
                failwithf "Invalid (or no) output path specified. Path must end with '.dll'"
        | OutputMode.CreateSourceFile ->
            if config.OutputPath.EndsWith ".fs" then ()
            elif config.OutputPath.EndsWith ".fsx" then ()
            else
                failwithf "Invalid (or no) output path specified. Path must end with '.fs' or '.fsx'"
        | _ -> ()

        // Go build if needed
        if config.IsValid() then
            lock state
                (fun () ->
                    // Cache the modified times for the source (accessing this
                    // requires hitting the disk multiple times)
                    let mt = config.SourceModifiedTimes
                    let build =
                        // Build if the state does not contain this config
                        if not <| state.ContainsKey config then true
                        else
                            // If the state contains this config...
                            let (x,_,_) = state.[config]
                            // Generate a set from the old and new modified
                            // times
                            let x = x |> Set.ofList
                            let y = mt |> Set.ofList
                            // Build if the two sets have differing file counts
                            x.Count <> y.Count ||
                            // Or if the union of the two sets is not the same
                            // size as either set
                            (Set.intersect x y).Count <> x.Count
                    // If any of the above says to build, then go build
                    if build then
                        let oldWD = Environment.CurrentDirectory
                        try
                            Environment.CurrentDirectory <- config.WorkingDir
                            let (file, file') = buildAssembly config (ty.Namespace, y.[y.Length - 1])
                            state.[config]  <- (mt, (file |> Option.map Assembly.LoadFrom), (Assembly.LoadFrom file'))
                            currentAssembly <- Some config
                        finally
                            Environment.CurrentDirectory <- oldWD
                )
        state.[config]
        |> function (_,_,x) -> x
        |> fun x -> x.GetType(ty.Namespace + "." + y.[y.Length - 1])




    do Environment.CurrentDirectory <- tpConfig.ResolutionFolder


    interface ITypeProvider with
        // This event is currently not used, but needs to be supplied
        [<CLIEvent>]
        member __.Invalidate =
            invalidateEvent.Publish

        // Returns a copy of the namespaces currently provided
        member __.GetNamespaces () =
            let ret = providedNamespaces()
            ret |> Array.copy

        // Always return the contents of the current assembly rather than the
        // specified assembly. This is done because the current assembly will
        // contain the expanded types, whereas the specified assembly will
        // typically be the "dummy" assembly used to appease the type provider
        // work-flow.
        member __.GetGeneratedAssemblyContents (_) =
            match currentAssembly with
            | None -> null
            | Some x ->
                match x.OutputMode with
                | OutputMode.BuildIntoProject ->
                    state.[x]
                    |> function (_,x,_) -> x
                    |> Option.map (fun x -> File.ReadAllBytes x.Location)
                    |> Option.toObj
                | OutputMode.CreateAssembly | OutputMode.CreateSourceFile ->
                    File.ReadAllBytes (state.[x] |> function (_,_,x) -> x).Location
                | _ -> failwithf "Invalid OutputMode value"

        // Tells the compiler how to handle calls to functions, constructors,
        // etc. for provided type information
        member __.GetInvokerExpression (mb : MethodBase, args) =
            let args = args |> Array.toList
            match mb with
            | :? ConstructorInfo as ci ->
                Expr.NewObject(ci, args)
            | :? MethodInfo as mi ->
                if mi.IsStatic then
                    Expr.Call(mi, args)
                else
                    Expr.Call(args.Head, mi, args.Tail)
            | _ ->
                failwithf "The definition for '%s' in the '%s' type is not a constructor or method"
                    mb.Name
                    (mb.GetType().FullName)

        // Returns the static type parameters for the specified type
        member __.GetStaticParameters (ty) =
            let sourceXml =
                sprintf
                    "<summary>The absolute or relative path to a source file, a project file, or a comma-delimited list of source files. Paths are relative to:\n\n%s</summary>"
                    Environment.CurrentDirectory
            if ty = typeof<Expand> then
                let f = buildStaticParameter
                let src = f "Source"           (None : string option            ) sourceXml
                let cwd = f "WorkingDirectory" (Some ""                         ) "An absolute path to use as the working directory to use while processing files. If provided, all source file paths, library references, and output paths will be relative to this path."
                let exc = f "ExcludeSource"    (Some ""                         ) "Source files to exclude from use; primarily used to exclude files that use this type provider when the main source is pointed to the project file"
                let ref = f "References"       (Some ""                         ) "Any library references required by the source"
                let flg = f "CompilerFlags"    (Some ""                         ) "Any special compiler flags that need to be passed to fsc.exe"
                let tmo = f "CompilerTimeout"  (Some 60                         ) "How many seconds to wait for fsc to run when called. Note that fsc is called once for processing templates, and a second time if the output mode is set to any mode other than CreateSourceFile."
                let pth = f "OutputPath"       (Some ""                         ) "The output path to use when OutputMode is set to CreateAssembly or CreateSourceFile"
                let out = f "OutputMode"       (Some OutputMode.BuildIntoProject) "How the expanded source is to be presented for use"
                [| src; cwd; exc; ref; flg; tmo; pth; out; |]
            else
                [| |]

        // Applies the given static type arguments for the specified type
        member __.ApplyStaticArguments (t, y, a) =
            try
                applyStaticArguments t y a
            with
            | e ->
                let rec getInner (e : exn) =
                    match e.InnerException with
                    | null -> sprintf "%s: %s" (e.GetType().FullName) e.Message
                    | ie -> sprintf "%s: %s -- Inner Exception:\n%s" (e.GetType().FullName) e.Message (getInner ie)
                failwith <| getInner e


    interface IDisposable with
        member __.Dispose () = ()

[<assembly:TypeProviderAssembly>]
do ()
