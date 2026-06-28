using SolidWorks.Interop.sldworks;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Threading;
using Xunit.Abstractions;
using Xunit.Runners;

namespace TestRunner
{
    static public class Program
    {
        // We use consoleLock because messages can arrive in parallel, so we want to make sure we get
        // consistent console output.
        static readonly object consoleLock = new object();

        // Use an event to know when we're done
        static readonly ManualResetEvent finished = new ManualResetEvent(false);

        // Start out assuming success; we'll set this to 1 if we get a failed test
        static int result = 0;

        static string TestNameFilter = "";

        [SuppressMessage(
            "Design",
            "CA1031:Do not catch general exception types",
            Justification = "The test runner entry point must print startup failures instead of crashing silently.")]
        public static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception ex)
            {
                PrintException(ex, "");
                return 1;
            }
        }

        private static int Run(string[] args)
        {
            string solutionDir = FindSolutionDirectory(AppDomain.CurrentDomain.BaseDirectory);

            string testAssembly = Path.Combine(solutionDir, "SW2URDF", "bin", "x64", "Debug", "SW2URDF.dll");
            string typeName = null;

            using (var runner = AssemblyRunner.WithAppDomain(testAssembly))
            {
                if (null != args && args.Length > 0)
                {
                    TestNameFilter = args[0];
                    runner.TestCaseFilter += FilterByClass;
                }
                runner.OnDiscoveryComplete = OnDiscoveryComplete;
                runner.OnExecutionComplete = OnExecutionComplete;
                runner.OnTestFailed = OnTestFailed;
                runner.OnTestSkipped = OnTestSkipped;

                Console.WriteLine("Discovering...");
                runner.Start(typeName);

                finished.WaitOne();
                finished.Dispose();
                return result;
            }
        }

        private static string FindSolutionDirectory(string startDirectory)
        {
            string directory = Path.GetFullPath(startDirectory);
            for (int i = 0; i < 12 && !String.IsNullOrWhiteSpace(directory); i++)
            {
                if (File.Exists(Path.Combine(directory, "SW2URDF.sln")))
                {
                    return directory;
                }

                DirectoryInfo parent = Directory.GetParent(
                    directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (parent == null)
                {
                    break;
                }
                directory = parent.FullName;
            }

            throw new DirectoryNotFoundException(
                "Could not find SW2URDF.sln above " + startDirectory);
        }

        private static void PrintException(Exception ex, string indent)
        {
            if (ex == null)
            {
                return;
            }

            Console.Error.WriteLine(indent + "Unhandled exception type: " + ex.GetType().FullName);
            Console.Error.WriteLine(indent + "Message: " + ex.Message);
            if (!String.IsNullOrWhiteSpace(ex.StackTrace))
            {
                Console.Error.WriteLine(indent + "Stack:");
                Console.Error.WriteLine(ex.StackTrace);
            }

            ReflectionTypeLoadException loaderException = ex as ReflectionTypeLoadException;
            if (loaderException != null && loaderException.LoaderExceptions != null)
            {
                for (int i = 0; i < loaderException.LoaderExceptions.Length; i++)
                {
                    Console.Error.WriteLine(indent + "Loader exception " + i + ":");
                    PrintException(loaderException.LoaderExceptions[i], indent + "  ");
                }
            }

            if (ex.InnerException != null)
            {
                Console.Error.WriteLine(indent + "Inner exception:");
                PrintException(ex.InnerException, indent + "  ");
            }
        }

        public static bool FilterByClass(ITestCase testCase)
        {
            if (null != testCase && testCase.DisplayName.Contains(TestNameFilter))
            {
                return true;
            }
            return false;
        }

        static void OnDiscoveryComplete(DiscoveryCompleteInfo info)
        {
            lock (consoleLock)
                Console.WriteLine($"Running {info.TestCasesToRun} of {info.TestCasesDiscovered} tests...");
        }

        static void OnExecutionComplete(ExecutionCompleteInfo info)
        {
            lock (consoleLock)
                Console.WriteLine(
                    $"Finished: {info.TotalTests} tests in" + 
                    $"{Math.Round(info.ExecutionTime, 3)}s " + 
                    $"({info.TestsFailed} failed, " + 
                    $"{info.TestsSkipped} skipped)");

            finished.Set();
        }

        static void OnTestFailed(TestFailedInfo info)
        {
            lock (consoleLock)
            {
                Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine("[FAIL] {0}: {1}", info.TestDisplayName, info.ExceptionMessage);
                if (info.ExceptionStackTrace != null)
                    Console.WriteLine(info.ExceptionStackTrace);

                Console.ResetColor();
            }

            result = 1;
        }

        static void OnTestSkipped(TestSkippedInfo info)
        {
            lock (consoleLock)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[SKIP] {0}: {1}", info.TestDisplayName, info.SkipReason);
                Console.ResetColor();
            }
        }
    }
}
