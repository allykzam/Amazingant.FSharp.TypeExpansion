namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("Amazingant.FSharp.TypeExpansion")>]
[<assembly: AssemblyProductAttribute("Amazingant.FSharp.TypeExpansion")>]
[<assembly: AssemblyDescriptionAttribute("Code generation tool that applies expansion functions to simple types")>]
[<assembly: AssemblyCopyrightAttribute("Copyright © 2016 amazingant (Anthony Perez)")>]
[<assembly: AssemblyVersionAttribute("0.4.0")>]
[<assembly: AssemblyFileVersionAttribute("0.4.0")>]
[<assembly: AssemblyInformationalVersionAttribute("0.4.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.4.0"
    let [<Literal>] InformationalVersion = "0.4.0"
