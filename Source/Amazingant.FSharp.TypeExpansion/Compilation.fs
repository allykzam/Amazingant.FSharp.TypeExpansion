namespace Amazingant.FSharp.TypeExpansion

open Amazingant.FSharp.TypeExpansion.Attributes

open System
open System.Collections.Generic
open System.IO
open System.Reflection
open System.Xml


[<AutoOpen>]
module internal Compilation =
    type File with
        static member NotExists (path : string) =
            not <| File.Exists path

    let notEmpty = Seq.isEmpty >> not
    let joinLines    (x : string seq) = String.Join("\n"  , x)
    let joinTwoLines (x : string seq) = String.Join("\n\n", x)
    let splitValues (x : obj) = (x :?> string).Split ([|','; '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries) |> Array.toList
    let strReplace (a : string) (b : string) (x : string) = x.Replace(a, b)
    let buildRefs = Seq.map (splitValues >> List.head) >> Seq.distinctBy (Path.GetFileName >> strReplace ".dll" "") >> Seq.collect (fun x -> ["-r";x]) >> Seq.toList
    let switch f x y = f y x
    // Uses F#'s static type constraint feature to check for a specified
    // attribute type on anything that has the GetCustomAttributes function;
    // e.g. System.Type, System.Reflection.MethodInfo, etc.
    let inline hasAttribute< ^R when ^R : (member GetCustomAttributes : Type -> bool -> obj [])>
        (a : Type) (r : ^R) =
            (^R : (member GetCustomAttributes : Type -> bool -> obj []) (r, a, false))
            |> notEmpty


    // Helper type for processing the user-specified source path
    type internal CompileSource (path : string, omitFiles : string list) =
        static let projFiles = Dictionary<string, (DateTime * (string list * string list))>()
        let (|Project|List|File|) (file : string) =
            let isProj = file.EndsWith ".fsproj"
            let isList = file.Contains "," || file.Contains "\n" || file.Contains "\r"
            let isFile = (not <| file.Contains ",") && (file.EndsWith ".fsx" || file.EndsWith ".fs")
            match isProj, isList, isFile with
            |  true, false, false -> Project file
            | false,  true, false -> List (splitValues file |> List.filter (fun x -> omitFiles |> Seq.contains x |> not))
            | false, false,  true -> File file
            | _ ->
                failwithf "Provided source path does not appear to be valid; should be a project file, a source file, or a comma-separated list of paths"

        member __.FilesAndRefs : (string list * string list) =
            match path with
            | File x -> ([x], [])
            | List xs -> (xs, [])
            | Project x ->
                if File.NotExists x then
                    failwithf "Provided source path could not be found. Paths are relative to:\n%s" Environment.CurrentDirectory
                lock projFiles
                    (fun () ->
                        let modTime = File.GetLastWriteTimeUtc(x)
                        match projFiles.TryGetValue x with
                        | (true, (x,y)) when x = modTime -> y
                        | _ ->
                            let doc = XmlDocument()
                            doc.Load x
                            let files =
                                System.Linq.Enumerable.Cast<XmlNode> (doc.GetElementsByTagName("Compile"))
                                |> Seq.map (fun x -> x.Attributes.["Include"].InnerText)
                                |> Seq.filter (fun x -> omitFiles |> Seq.contains x |> not)
                                |> Seq.toList
                            let refs =
                                System.Linq.Enumerable.Cast<XmlNode> (doc.GetElementsByTagName("Reference"))
                                |> Seq.map (fun x -> x.Attributes.["Include"].InnerText)
                                |> Seq.filter (fun x -> x <> ((typeof<CompileSource>.Assembly).GetName().Name))
                                |> Seq.toList
                            projFiles.[x] <- (modTime, (files, refs))
                            files, refs
                    )


    // This finds a copy of FSharp.Core that has the required optdata and
    // sigdata files, as well as the locations of this assembly, the attributes
    // assembly, and mscorlib. These base references are required for all of the
    // compiling done.
    let requiredRefs =
        let fsCoreMain = @"C:\Program Files\Reference Assemblies\Microsoft\FSharp\.NETFramework\v4.0\4.4.0.0\FSharp.Core.dll"
        let fsCorex86 = @"C:\Program Files (x86)\Reference Assemblies\Microsoft\FSharp\.NETFramework\v4.0\4.4.0.0\FSharp.Core.dll"
        let fsCore =
            if File.Exists fsCoreMain then
                fsCoreMain
            elif File.Exists fsCorex86 then
                fsCorex86
            else
                failwithf "Version 4.0 of F# does not appear to be installed on this system"
        [
            typeof<Attributes.ExpandableTypeAttribute>.Assembly.Location;
            typeof<CompileSource>.Assembly.Location;
            typeof<string>.Assembly.Location;
            fsCore;
        ]


    let fscLocation =
        let paths =
            [
                @"C:\Program Files (x86)\Microsoft SDKs\F#\4.0\Framework\v4.0\fsc.exe";
                @"C:\Program Files\Microsoft SDKs\F#\4.0\Framework\v4.0\fsc.exe";
            ]
        paths
        |> Seq.filter File.Exists
        |> Seq.tryHead
        |> function
           | None -> failwithf "Cannot find F# compiler"
           | Some x -> x
    let runFsc (args : string seq) (timeout : int) =
        use proc = new System.Diagnostics.Process()
        let si = System.Diagnostics.ProcessStartInfo(fscLocation)
        proc.StartInfo <- si
        si.UseShellExecute <- false
        si.CreateNoWindow <- true
        si.RedirectStandardError <- true
        si.RedirectStandardInput <- true
        si.RedirectStandardOutput <- true
        si.Arguments <-
            args
            |> Seq.map (fun x -> if x.Contains(" ") then sprintf "\"%s\"" x else x)
            |> fun x -> String.Join(" ", x)
        if not <| proc.Start() then
            failwithf "Could not run fsc.exe"
        // If fsc takes longer than XX seconds to compile, assume something is
        // wrong and kill it.
        if not <| proc.WaitForExit(timeout * 1000) then
            try
                proc.Kill()
            with
            | _ -> ()
            // Throw an error to the user so they know that fsc is taking a long
            // time to compile.
            failwithf "Compiler took longer than %i seconds to run? There may be a problem with the code being expanded." timeout
        // If fsc finished in under 60 seconds, proceed!
        let err = proc.StandardError.ReadToEnd()
        if not <| String.IsNullOrWhiteSpace err then
            failwithf "Compiler error:\n\n%s" (err.Replace("\r", ""))


    let partialBuild (source : CompileSource) refs flags fscTimeout =
        let tempLibPath = Path.ChangeExtension(Path.GetTempFileName(), ".dll")
        let args = [ "--noframework"; "-a"; sprintf "-o:%s" tempLibPath; "--target:library"; "--debug" ]
        let (files, extraRefs) = source.FilesAndRefs
        let refs = buildRefs (requiredRefs @ refs @ extraRefs)
        let args = args @ refs @ files @ flags |> Seq.toArray
        runFsc args fscTimeout
        if File.NotExists tempLibPath then
            failwithf "Could not compile source"
        else
            Assembly.LoadFrom tempLibPath


    // Checks the given method to see if it can be used for expansion
    let isValidExpander (mi : MethodInfo) =
        let ps = mi.GetParameters()
        // Must have the TypeExpander attribute
        mi |> hasAttribute typeof<TypeExpanderAttribute> &&
        // Must return a string
        mi.ReturnType = typeof<string> &&
        // Must take exactly one parameter
        ps.Length = 1 &&
        // Parameter must be of type System.Type
        ps.[0].ParameterType = typeof<Type>


    // Processes the given source, passes types through expanders, and returns
    // the expanded source code
    let processFiles source refs flags fscTimeout =
        let asm = partialBuild source refs flags fscTimeout
        // Get the types that can be expanded
        let targets =
            asm.DefinedTypes
            |> Seq.filter (hasAttribute typeof<ExpandableTypeAttribute>)
            |> Seq.toArray
        // Get the functions that can perform expansion
        let fs =
            let flags =
                BindingFlags.Public       |||
                BindingFlags.Static       |||
                BindingFlags.DeclaredOnly
            asm.DefinedTypes
            |> Seq.collect (fun x -> x.GetMethods(flags))
            |> Seq.filter isValidExpander

        // For each function, collect the expanded version of every target type,
        // filter out any results that are empty or null, and concatenate the
        // results together with some empty space
        fs
        |> Seq.collect
            (fun x ->
                let xAttr : TypeExpanderAttribute = x.GetCustomAttribute()
                targets
                |> Seq.map
                    (fun y ->
                        let yAttr : ExpandableTypeAttribute = y.GetCustomAttribute()
                        if yAttr.CanUseTemplate(xAttr.Name, xAttr.RequireExplicitUse) then
                            try
                                x.Invoke(null, [|y|]) :?> string
                            with
                            | :? System.Reflection.TargetInvocationException as ex ->
                                failwithf "Encountered a %s processing type '%s' with expander '%s.%s':\n%s\n\n%s"
                                    (ex.InnerException.GetType().FullName)
                                    y.FullName
                                    x.DeclaringType.FullName
                                    x.Name
                                    ex.InnerException.Message
                                    ex.InnerException.StackTrace
                        else
                            ""
                    )
            )
        |> Seq.filter (String.IsNullOrWhiteSpace >> not)
        |> joinTwoLines


    let buildStaticParameter<'t> name (defaultValue : 't option) xmlDoc =
        // TODO: Handle XML documentation
        let def = box <| defaultArg defaultValue Unchecked.defaultof<'t>
        {
            new ParameterInfo() with
                override __.Name            with get () = name
                override __.ParameterType   with get () = typeof<'t>
                override __.Position        with get () = 0
                override __.RawDefaultValue with get () = def
                override __.DefaultValue    with get () = def
                override __.Attributes
                    with get () =
                        match defaultValue with
                        | Some _ -> ParameterAttributes.Optional
                        | _ -> ParameterAttributes.None
        }