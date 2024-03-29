#### 2.3.0 - 2022-04-18
* Adjust FSharp.Core location detection to check where the current compilation
  environment is getting FSharp.Core from, and additionally add in some paths
  where Visual Studio 2022 and 2019 install copies of FSharp.Core.

#### 2.2.1 - 2022-02-24
* Adjust temporary assembly generation to use the copy of FSharp.Core that is in
  your project, rather than the one that the F# compiler chooses. This should
  ensure that projects can still be built in Visual Studio 2019 on systems where
  the F# compiler from Visual Studio 2022 is used.

#### 2.2.0 - 2022-02-24
* Use the Visual Studio 2022 version of the F# compiler internally during code
  generation, when it can be found.
* Add additional path(s) where the Visual Studio 2019 version of the F# compiler
  can be found.

#### 2.1.0 - 2020-04-22
* Add support for loading FSharp.Core from nested package directories that
  target .NET Framework 4.5 or .NET Standard 2.0, in order to support being used
  in a project that uses recent versions of FSharp.Core.

#### 2.0.0 - 2020-03-09
* #21: Update the Attributes project to target .NET Standard 2.0, and update the
  type provider to target .NET Framework 4.7. These changes enable use of the
  type provider in projects that target .NET Core.
* Use the Visual Studio 2019 version of the F# compiler internally during code
  generation.
* Update project files to use the newer SDK style.
* #20: Fix errors with temporary file paths being shown when active type
  expansion templates do not produce any output.

#### 1.0.3 - 2017-02-24
* Fix both project files using normal references to FSharp.Core

#### 1.0.2 - 2017-02-24
* Fix the Attributes project referencing FSharp.Core 4.4.0.0, rather than using
  the paket-managed reference

#### 1.0.1 - 2017-02-24
* Pass the `--nocopyfsharpcore` parameter to `fsc` to prevent compile errors
  when `fsc` attempts to put our copy of `FSharp.Core.dll` into `%TEMP%`

#### 1.0.0 - 2017-02-22
* No major feature changes for this release, but the referenced version of
  `FSharp.Core` has changed. This is likely to be a breaking change for
  consumers of this type provider, hence the version bump.
* #19: Update `FSharp.Core` paket reference to 4.1.0
* #18: Update detection and use of `FSharp.Core` and `fsc.exe` to look in the
  appropriate paths for F# 4.1 as installed by Visual Studio 2017

#### 0.8.0 - 2016-12-27
* #17: When the output mode is set to `CreateSourceFile`, the generated source
  now contains an `#if INTERACTIVE` block with `#load` and `#r` directives to
  ensure that the generated source can be used without modification

#### 0.7.3 - 2016-11-18
* #16: Add support for running type provider on macOS systems with Visual Studio
  for Mac by adding better detection of FSharp.Core and fsc/fsharpc
* When running on a non-Windows platform, attempt to use mono to run fsc/fsharpc
* Adjust target working directory handling to not fidget with the current
  working directory for the type provider; now adjusting file paths and
  indicating the desired working directory when invoking fsc/fsharpc

#### 0.7.2 - 2016-10-19
* #15: Fix dummy library not building due to missing the compiler timeout
  parameter

#### 0.7.1 - 2016-10-19
* #14: Add file path for F# compiler when running on 32-bit Windows

#### 0.7.0 - 2016-09-15
* #12: Add new `CompilerTimeout` static parameter with 60-second default; if
  fsc.exe runs longer than that during any single call to it, it will be killed
  and an exception thrown.

#### 0.6.0 - 2016-09-14
* #11: Add new `WorkingDirectory` static parameter to help path issues when
  using the provider with multiple projects in a single solution

#### 0.5.0 - 2016-09-07
* Add TypeIntrospection module; includes a helper function for use in templates
  which gets the "code-friendly" name of a given type

#### 0.4.1 - 2016-09-06
* #3: License project under the MIT license

#### 0.4.0 - 2016-09-02
* #8: Unwrap exceptions thrown by expanders; no longer shows a
  TargetInvocationException. Adjusted intermediate build stage to include
  debugging symbols so that stack traces include source files and line numbers.
* #10: Fix compiler flags not being passed during intermediate builds; users
  should now be able to specify symbols by adding e.g. `--define:TYPE_EXPANSION`
  to the build flags, and then check for that in their code with
  `#if TYPE_EXPANSION`

#### 0.3.0 - 2016-08-29
* #4: Add a comment header to generated files when set to `CreateSourceFile`
* #6: Adjust files, references, etc. to split on line break characters in
  addition to commas

#### 0.2.0 - 2016-08-26
* #1: Add support for using a project file as the source
    * Requires specifying an ExcludeSource file or comma-delimited list of
      files, so that the files that call to the type provider are not compiled
      too
* #2: Removed dependency on FSharp.Compiler.Service so that this will run on
  systems without the right version of MSBuild on them (my laptop)
* Updated file tracking so that expansion happens any time that the modified
  time for the source file(s) changes
  * If this happens too much, it may be worth commenting out the call to the
    type provider

#### 0.1.0 - 2016-08-22
* Initial release
