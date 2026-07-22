# Testing SW2URDF

This project programmatically runs the tests of the SW2URDF project.
The tests rely on the models provided in the examples directory, so any changes to those files may cause these tests to fail.
Update any tests to reflect corresponding changes in the models.

## To Build

There is a test that checks that the git repo is not dirty, and all files have been committed.
To pass that test, you need to commit all files, then rebuild the solution.

When you build the solution, you should see two successful builds, SW2URDF and TestRunner.

## To Run

Run the TestRunner executable, it will locate the SW2URDF Dll automatically.

    TestRunner\bin\Debug\net452>TestRunner.exe

For an isolated build, set `SW2URDF_TEST_ASSEMBLY` to the absolute path of the
newly built `SW2URDF.dll`. This prevents the runner from silently testing a
stale DLL when the normal output directory is in use by SolidWorks.

If you only want to run a subset of tests, the first argument of TestRunner.exe is an optional filter parameter.
Any test with a fully qualified NameSpace.ClassName.FunctionName that contains the provided string will be run.
For example, to run just the versioning tests.

    TestRunner\bin\Debug\net452>TestRunner.exe TestVersioning
