namespace Amazingant.FSharp.TypeExpansion.Attributes

open System

/// <summary>
/// Flags a type as available for expansion by methods marked with the
/// <see cref="TypeExpanderAttribute" /> attribute.
/// </summary>
/// <param name="onlyUseTemplates">
/// If specified, only the templates specified are used for this base type.
/// </param>
/// <param name="excludeTemplates">
/// If specified, the templates specified are never applied to this base type.
/// </param>
[<AttributeUsage(
    AttributeTargets.Class     |||
    AttributeTargets.Struct    |||
    AttributeTargets.Interface |||
    AttributeTargets.Enum
    )>]
type ExpandableTypeAttribute(onlyUseTemplates : string [], excludeTemplates : string []) =
    inherit Attribute()
    let onlyUse = onlyUseTemplates |> Option.ofObj
    let exclude = excludeTemplates |> Option.ofObj

    new () = ExpandableTypeAttribute(null, null)
    new (onlyUseTemplates) = ExpandableTypeAttribute(onlyUseTemplates, null)

    member __.CanUseTemplate (name : string option, reqExplicitUse : bool) =
        match name, reqExplicitUse with
        // If the template has no name and does not need to be explicitly
        // specified, and the base type does not specify an "only use" list,
        // then the template can be used!
        | None, false -> onlyUse.IsNone
        // If the template has no name but says it must be explicitly specified,
        // then it cannot be used.
        | None, true -> false
        // If the template has a name and requires explicit use
        | Some name, true ->
            // Return true if the base type called for this template
            onlyUse.IsSome && onlyUse.Value |> Seq.contains name
        // If the template has a name and does not require explicit use
        | Some name, false ->
            match onlyUse, exclude with
            // True if the base type does not care what templates are used
            | None, None -> true
            // If the base type has an "only use" list, check the list
            | Some xs, None -> xs |> Seq.contains name
            // If the base type has an exclusion list, check the list
            | None, Some xs -> xs |> Seq.contains name |> not
            // If the base type has both lists, check them both!
            | Some xs, Some ys ->
                (xs |> Seq.contains name) &&
                (ys |> Seq.contains name |> not)



/// <summary>
/// Flags a method for use in expanding types marked with the
/// <see cref="ExpandableTypeAttribute" /> attribute.
/// </summary>
/// <param name="name">
/// Indicates the name for this type expander, so that base types can opt-in or
/// opt-out of using this template.
/// </param>
/// <param name="requireExplicitUse">
/// If true, this template is only applied to base types that have explicitly
/// requested its use.
/// </param>
[<AttributeUsage(AttributeTargets.Method)>]
type TypeExpanderAttribute(name : string, requireExplicitUse : bool) =
    inherit Attribute()

    new () = TypeExpanderAttribute(null, false)
    new (name) = TypeExpanderAttribute(name, false)
    new (requireExplicitUse) = TypeExpanderAttribute(null, requireExplicitUse)

    member __.Name =
        if System.String.IsNullOrWhiteSpace name
        then None
        else Some name
    member __.RequireExplicitUse = requireExplicitUse
