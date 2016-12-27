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
