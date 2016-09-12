Type-Expansion
==============

This library is a code generator, which uses a template-driven system to
"expand" types. The types that need to be expanded -- as well as the functions
that perform the expansions -- are user-provided. That is, this provider does
not come with any templates enabled.

This expansion system allows creating short and simple type definitions with a
single complex template, with larger resulting types that contain additional
behavior.

For some working examples, please take a look at the accompanying
[templates][templates-proj] project, which includes some real-world samples that
are being used for real-world code.


Examples
--------

In the following code, a simple record type is defined and marked with the
`ExpandableType` attribute. Due to the particular template that will be applied,
the simple type includes a static property named `DefaultValue` which provides,
as its name implies, a default value.

```FSharp
[<ExpandableType>]
type TestFile =
    {
        FileName : string;
        Size : int;
        Processing : bool;
    }
    static member DefaultValue =
        { FileName = ""; Size = 0; Processing = false; }
```

In a WPF application, a template could automatically "expand" this type and
build the following type:

```FSharp
type TestFile_ViewModel(content : TestFile) =
    let propertyChanged = Event<_,_>()
    let mutable innerValue : TestFile = content
    new () = TestFile_ViewModel(TestFile.DefaultValue)

    member private __.PrivateInnerValue
        with get () = innerValue
        and set (value) = innerValue <- value

    member this.InnerValue
        with get () = this.PrivateInnerValue
        and set (value) =
            this.PrivateInnerValue <- value
            this.RaisePropertyChanged("FileName")
            this.RaisePropertyChanged("Size")
            this.RaisePropertyChanged("Processing")

    member this.FileName
        with get () = this.PrivateInnerValue.FileName
        and set (value) =
            this.PrivateInnerValue <- { this.PrivateInnerValue with FileName = value }
            this.RaisePropertyChanged("FileName")

    member this.Size
        with get () = this.PrivateInnerValue.Size
        and set (value) =
            this.PrivateInnerValue <- { this.PrivateInnerValue with Size = value }
            this.RaisePropertyChanged("Size")

    member this.Processing
        with get () = this.PrivateInnerValue.Processing
        and set (value) =
            this.PrivateInnerValue <- { this.PrivateInnerValue with Processing = value }
            this.RaisePropertyChanged("Processing")

    member private this.RaisePropertyChanged(propertyName : string) =
        propertyChanged.Trigger(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName))

    interface System.ComponentModel.INotifyPropertyChanged with
        [<CLIEvent>]
        member __.PropertyChanged = propertyChanged.Publish
```

The new "expanded" type now implements `INotifyPropertyChanged` so that it can
be used in WPF easily, and allows setting each of the fields contained in the
original type. Note that while in this particular example, the original type
definition is actually used as the backing data storage for the new type, this
does not have to be the case! The new type could just as easily have ignored the
presence of the original type, and used e.g. `let mutable FileName =
Unchecked.defaultOf<string>` as the backing data storage for each field.
Likewise, when using `FSharp.ViewModule`, each field could be backed by
`self.Factory.Backing(<@ self.FileName @>, Unchecked.defaultOf<string>)`
instead.

As another example, using the same short type, the following extension method
could be created:

```FSharp
type TestFile with
    static member GetFromSql () =
        query {
            for file in db.AllFiles do
            select { FileName = file.FileName; Size = file.Size; Processing = false; }
        } |> Seq.toList
```

The result is a new extension method on the `TestFile` type, which loads file
information from the `AllFiles` database table. Of course if the project does
not use query expressions, other database connectivity tools can be used just as
easily.


Building Templates
------------------

Building templates is as simple as defining a function that takes a
`System.Type` parameter and returns a `string` value, and adding the
`TypeExpander` attribute. These functions can be as simple or as complex as
needed. On one extreme, the bare minimum required for a template function is
shown below:

```FSharp
[<TypeExpander>]
let UselessTemplate (_ : System.Type) = ""
```

However, this sample code is not very useful. If the simple type was also given
a custom attribute, such as `[<SqlTable("AllFiles")>]`, one could use the
following template to generate the query expression code shown previously:

```FSharp
[<TypeExpander>]
let QueryExpressionTemplate (t : System.Type) =
    match t.GetCustomAttributes(typeof<SqlTableAttribute>, false) with
    | [| x |] ->
        match x with
        | :? SqlTableAttribute as x ->
            let props =
                t.GetProperties()
                |> Seq.map (fun x -> sprintf "%s = file.%s; " x.Name x.Name)
                |> System.String.Concat
            sprintf """
type %s with
    static member GetFromSql () =
        query {
            for file in db.%s do
            select { %s }
        } |> Seq.toList
"""
                t.Name
                x.TableName
                props

        | _ -> ""
    | _ -> ""
```

Note that the indentation in this code suddenly shifts as the call to `sprintf`
begins. Template functions can work around this by using literal line breaks in
their strings via `\n`, but indentation still needs to be provided for the
generated code. If one is already familiar with it, the `SquirrelMix` code from
the [`MixinProvider`][mixin-provider] project can be used to streamline this
process.


How to setup
------------

As with any other project or type provider, the first step is to add a reference
to this library. Next, somewhere in the project that will use the provider, add
a type alias for the type provider as follows:

```FSharp
type Test = Amazingant.FSharp.TypeExpansion.Expand<"SourceFile.fsx">
```

The file `SourceFile.fsx` will now be processed by the type provider. Any types
found in the source with the `ExpandableType` attribute will be processed, and
any functions found with the `TypeExpander` attribute will be used to do said
processing. Note that this is a many-to-many relationship; if there are five
base types and five expansion functions, twenty-five new types will be
generated. This can be controlled with the optional parameters in the two
attributes, describe below in the `Template Control` section.

By default the type provider attempts to embed the finished type definitions
into the calling project; since this finished information includes the original
types, the source file(s) specified should be ones that are not compiled into
the project.

Alternatively, the `OutputMode` parameter can be used along with the
`OutputPath` parameter. When the mode is set to `CreateAssembly`, the finished
type information (in addition to its source) will be output to a new library at
the specified path; this library can then be referenced instead of the project
which is using the type provider, as it will contain all of the original source
that was specified. When the mode is instead set to `CreateSourceFile`, the
finished type information will be output to an F# source file at the specified
path, and will _**NOT**_ contain any of the original source information. This is
the most useful of the three modes, as the expanded source code is now available
for source control and debugging, but use of this mode means that the type
provider must be invoked whenever any base type or template function is changed,
else the expanded source that goes to source control will not match what will be
built.

The source file specified can point to an F# source file (extension should be
either `.fs` or `.fsx`), a comma-delimited list of source files, or a project
file. Note that project file support is limited, but will cause all of the
appropriate source files and references to be used when compiling. If the
`OutputMode` is set to `CreateSourceFile` while the provider has been pointed to
a project file, the provider will attempt to exclude the output file path when
compiling, to avoid recursively calling itself (in case any of the templates use
the type provider as well).


Template Control
----------------

Since a small number of template functions and base types can quickly amount to
a very long list of generated types, the attributes used in this provider allow
for some control over which templates are applied to which base types. Due to
some oddities in how optional parameters behave in F#, the parameters are not
actual optional parameters, there just happens to be a handful of constructors
that hopefully make them easier to use.

For a specific base type, the `onlyUseTemplates` and `excludeTemplates`
parameters are available:

```FSharp
[<ExpandableType(
    onlyUseTemplates = [| "ViewModel"; |],
    excludeTemplates = [| "SqlQuery"; |]
    )>]
type TestFile =
    ...
```

When the `onlyUseTemplates` parameter is specified, only templates specified in
the given array will be applied to this base type. When the `excludeTemplates`
parameter is specified, the templates specified in the given array will
_**NEVER**_ be applied to this base type.

For the template functions, the `name` and `requireExplicitUse` parameters are
available:

```FSharp
[<TypeExpander(
    name = "ViewModel",
    requireExplicitUse = true
    )>]
let ViewModelTemplate (t : System.Type) =
    ...
```

To use the parameters for the `ExpandableType` attribute, the `TypeExpander`
attribute for at least one template should have the `name` parameter specified,
but it is not required. However, if the `requireExplicitUse` parameter is
specified as `true` for any template functions, those template functions will
only be used on base types that specified the template's name in their
`onlyUseTemplates` parameter. By default, the `requireExplicitUse` parameter is
set to `false`.

Note that setting the `requireExplicitUse` parameter for a template function to
`true` and not supplying a `name` value -- or specifying a null/empty string for
the template name -- will result in the template never being applied to any base
types.


License
-------

This project is Copyright © 2016 Anthony Perez a.k.a. amazingant, and is
licensed under the MIT license. See the [LICENSE file][license] for more
details.


[templates-proj]: https://github.com/amazingant/Amazingant.FSharp.TypeExpansion.Templates
[mixin-provider]: https://github.com/pezipink/MixinProvider
[license]: https://github.com/amazingant/Amazingant.FSharp.TypeExpansion/blob/master/LICENSE
