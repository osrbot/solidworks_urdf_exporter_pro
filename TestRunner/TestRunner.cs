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

        // A release gate must not pass when discovery silently returns no tests.
        static int testsDiscovered = 0;

        static string TestNameFilter = "";
        static bool ExcludeLiveSolidWorksTests;
        static string IsolatedTestLogFile;

        const string RequiresSolidWorksCollection = "Requires SW Test Collection";
        const string LiveSolidWorksTraitName = "Category";
        const string LiveSolidWorksTraitValue = "LiveSolidWorks";

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
            ConfigureIsolatedTestLog();

            string configuredAssembly = System.Environment.GetEnvironmentVariable(
                "SW2URDF_TEST_ASSEMBLY");
            string testAssembly = String.IsNullOrWhiteSpace(configuredAssembly)
                ? Path.Combine(solutionDir, "SW2URDF", "bin", "x64", "Debug", "SW2URDF.dll")
                : Path.GetFullPath(configuredAssembly);
            if (!File.Exists(testAssembly))
            {
                throw new FileNotFoundException(
                    "The SW2URDF test assembly was not found.",
                    testAssembly);
            }
            string typeName = null;

            using (var runner = AssemblyRunner.WithAppDomain(testAssembly))
            {
                ConfigureTestFilter(args);
                if (ExcludeLiveSolidWorksTests || !String.IsNullOrWhiteSpace(TestNameFilter))
                {
                    runner.TestCaseFilter += FilterTestCase;
                }
                runner.OnDiscoveryComplete = OnDiscoveryComplete;
                runner.OnExecutionComplete = OnExecutionComplete;
                runner.OnTestFailed = OnTestFailed;
                runner.OnTestFinished = OnTestFinished;
                runner.OnTestSkipped = OnTestSkipped;
                runner.OnTestStarting = OnTestStarting;

                Console.WriteLine("Discovering...");
                runner.Start(typeName);

                finished.WaitOne();
                finished.Dispose();
            }

            CleanupIsolatedTestLog();
            return result;
        }

        private static void ConfigureTestFilter(string[] args)
        {
            TestNameFilter = "";
            ExcludeLiveSolidWorksTests = false;
            if (args == null)
            {
                return;
            }

            foreach (string argument in args)
            {
                if (String.Equals(
                    argument,
                    "--exclude-live-solidworks",
                    StringComparison.OrdinalIgnoreCase))
                {
                    ExcludeLiveSolidWorksTests = true;
                    continue;
                }

                if (!String.IsNullOrWhiteSpace(argument))
                {
                    if (!String.IsNullOrWhiteSpace(TestNameFilter))
                    {
                        throw new ArgumentException(
                            "Only one test-name filter may be specified.");
                    }
                    TestNameFilter = argument;
                }
            }
        }

        private static void ConfigureIsolatedTestLog()
        {
            const string variableName = "SW2URDF_LOG_FILE";
            if (!String.IsNullOrWhiteSpace(
                System.Environment.GetEnvironmentVariable(variableName)))
            {
                return;
            }

            string logFile = Path.Combine(
                Path.GetTempPath(),
                "sw2urdf-tests",
                "sw2urdf-" + System.Diagnostics.Process.GetCurrentProcess().Id + ".log");
            System.Environment.SetEnvironmentVariable(variableName, logFile);
            IsolatedTestLogFile = logFile;
        }

        private static void CleanupIsolatedTestLog()
        {
            if (String.IsNullOrWhiteSpace(IsolatedTestLogFile))
            {
                return;
            }

            try
            {
                File.Delete(IsolatedTestLogFile);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
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

        public static bool FilterTestCase(ITestCase testCase)
        {
            if (testCase == null)
            {
                return false;
            }

            if (ExcludeLiveSolidWorksTests && IsLiveSolidWorksTest(testCase))
            {
                return false;
            }

            return String.IsNullOrWhiteSpace(TestNameFilter) ||
                testCase.DisplayName.Contains(TestNameFilter);
        }

        private static bool IsLiveSolidWorksTest(ITestCase testCase)
        {
            if (testCase.Traits != null &&
                testCase.Traits.TryGetValue(
                    LiveSolidWorksTraitName,
                    out System.Collections.Generic.List<string> categories) &&
                categories != null)
            {
                foreach (string category in categories)
                {
                    if (String.Equals(
                        category,
                        LiveSolidWorksTraitValue,
                        StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            ITestCollection collection = testCase.TestMethod?.TestClass?.TestCollection;
            return collection != null && String.Equals(
                collection.DisplayName,
                RequiresSolidWorksCollection,
                StringComparison.Ordinal);
        }

        static void OnDiscoveryComplete(DiscoveryCompleteInfo info)
        {
            testsDiscovered = info.TestCasesDiscovered;
            lock (consoleLock)
                Console.WriteLine($"Running {info.TestCasesToRun} of {info.TestCasesDiscovered} tests...");
        }

        static void OnExecutionComplete(ExecutionCompleteInfo info)
        {
            if (testsDiscovered <= 0 || info.TotalTests <= 0)
            {
                result = 1;
                Console.Error.WriteLine(
                    "[FAIL] No SW2URDF tests were discovered or executed; the release gate cannot pass.");
            }

            lock (consoleLock)
                Console.WriteLine(
                    $"Finished: {info.TotalTests} tests in " +
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

        static void OnTestStarting(TestStartingInfo info)
        {
            lock (consoleLock)
                Console.WriteLine("[RUN] {0}", info.TestDisplayName);
        }

        static void OnTestFinished(TestFinishedInfo info)
        {
            lock (consoleLock)
                Console.WriteLine("[DONE] {0}", info.TestDisplayName);
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
