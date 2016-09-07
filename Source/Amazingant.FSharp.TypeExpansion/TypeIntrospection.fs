namespace Amazingant.FSharp.TypeExpansion

open System

module TypeIntrospection =

    /// Simple types that have built-in aliases in F#
    let AliasedTypes =
        [
            typeof<      bool>,       "bool";
            typeof<      byte>,       "byte";
            typeof<     sbyte>,      "sbyte";
            typeof<     int16>,      "int16";
            typeof<    uint16>,     "uint16";
            typeof<     int  >,      "int"  ;
            typeof<    uint32>,     "uint32";
            typeof<     int64>,      "int64";
            typeof<    uint64>,     "uint64";
            typeof< nativeint>,  "nativeint";
            typeof<unativeint>, "unativeint";
            typeof<      char>,       "char";
            typeof<    string>,     "string";
            typeof<   decimal>,    "decimal";
            typeof<      unit>,       "unit";
            typeof<   float32>,    "float32";
            typeof<   float  >,    "float"  ;
            typeof<    single>,     "single";
            typeof<    double>,     "double";
        ] |> dict

    /// Big handler for converting a type into a string that represents its
    /// name, in such a way as to be valid F# code (and potentially nice-looking
    /// code as well)
    let rec GetTemplateFriendlyName (t : Type) : string =
        let  optType = typeof<int option  >.GetGenericTypeDefinition()
        let  seqType = typeof<int seq     >.GetGenericTypeDefinition()
        let listType = typeof<int list    >.GetGenericTypeDefinition()
        let  refType = typeof<int ref     >.GetGenericTypeDefinition()
        let  mapType = typeof<Map<int,int>>.GetGenericTypeDefinition()
        let funcType = typeof<int -> int  >.GetGenericTypeDefinition()
        let rec nestedType (t : Type) : string list * string list=
            // Handle arrays
            if t.IsArray then
                let t = t.GetMethod("Get").ReturnType
                let (l,r) = nestedType t
                "("::l, r@([" [])"])

            // If the type is contained in the "short" type dictionary, then there
            // is a built-in type alias for it; use that
            elif AliasedTypes.ContainsKey t then
                [AliasedTypes.[t]], []

            // If the type is not a generic type, just return its full name
            elif not t.IsConstructedGenericType then
                if t.IsGenericParameter && isNull t.FullName then
                    ["'"; t.Name], []
                else
                    [t.FullName], []

            else
                // For generics types, get the generic type definition
                let gt = t.GetGenericTypeDefinition()
                // These generic types are easy to handle
                let isSimpleGeneric =
                    gt = optType  ||
                    gt = seqType  ||
                    gt = listType ||
                    gt = refType
                // So handle the easy ones like this
                if isSimpleGeneric then
                    let (l,r) = nestedType (t.GetGenericArguments().[0])
                    let x =
                        if gt = optType then " option)"
                        elif gt = seqType then " seq)"
                        elif gt = refType then " ref)"
                        else " list)"
                    "("::l, r@([x])

                // If the type is a Map<'a,'b> ...
                elif gt = mapType then
                    let ga = t.GetGenericArguments()
                    let l = GetTemplateFriendlyName ga.[0]
                    let r = GetTemplateFriendlyName ga.[1]
                    ["Map<";l;", ";r;">"], []

                // If the type is a function, handle it nicely with arrows and such
                elif gt = funcType then
                    let ga = t.GetGenericArguments()
                    let l = GetTemplateFriendlyName ga.[0]
                    let r = GetTemplateFriendlyName ga.[1]
                    // If the second argument is another function, remove the
                    // parentheses so that `int -> int -> int` does not become
                    // `int -> (int -> int)`
                    let r =
                        if ga.[1].IsConstructedGenericType && ga.[1].GetGenericTypeDefinition() = funcType then
                            r.Substring(1, r.Length - 2)
                        else
                            r
                    ["(";l;" -> ";r;")"], []

                // If the type is a tuple, handle it nicely with asterisks
                elif gt.FullName.StartsWith("System.Tuple`") then
                    let ga =
                        t.GetGenericArguments()
                        |> Seq.map
                            (fun x ->
                                // For any argument which is itself a tuple (a sign
                                // that someone made a REALLY BIG tuple), remove the
                                // parentheses from the nested System.Tuple. Check
                                // for a null/empty FullName because generic types
                                // (e.g. 'a or ^T) will not have a FullName.
                                if not <| String.IsNullOrWhiteSpace x.FullName && x.FullName.StartsWith("System.Tuple`") then
                                    let n = GetTemplateFriendlyName x
                                    n.Substring(1, n.Length - 2)
                                else
                                    GetTemplateFriendlyName x
                            )
                        |> fun x -> String.Join(" * ", x)
                    ["(";ga;")"], []

                // For all other generic types, get the full name of the generic
                // type and re-construct it with its arguments
                else
                    let gn = gt.FullName.Split([|'`'|]) |> Array.head
                    let ga =
                        t.GetGenericArguments()
                        |> Seq.map GetTemplateFriendlyName
                        |> fun x -> String.Join(", ", x)
                    [gn;"<";ga;">"], []

        let (l,r) = nestedType t
        Seq.append l r |> String.Concat
