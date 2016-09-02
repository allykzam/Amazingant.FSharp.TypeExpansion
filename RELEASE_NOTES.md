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
