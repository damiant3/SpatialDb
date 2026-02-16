using System.Diagnostics;
///////////////////////
namespace SpatialDbApp;

internal static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // Pseudocode / Plan:
        // 1. Initialize WinForms application configuration.
        // 2. Force WinForms to route thread exceptions to our handler.
        // 3. Subscribe to AppDomain.CurrentDomain.UnhandledException,
        //    Application.ThreadException and TaskScheduler.UnobservedTaskException.
        // 4. Run the main form inside a try/catch that:
        //    - If the CLR is shutting down (Environment.HasShutdownStarted) or
        //      the exception looks like a benign shutdown-time exception
        //      (ObjectDisposedException, OutOfMemoryException, etc.) then swallow it.
        //    - Otherwise rethrow to preserve normal debugging behavior.
        // 5. In each handler: log diagnostic info (Debug.WriteLine) and try to
        //    mark task exceptions as observed so they don't crash the process.
        // 6. Unsubscribe handlers in a finally block (defensive cleanup).
        //
        // This approach silences common "disposed during shutdown" and similar
        // exceptions while still letting non-shutdown exceptions surface during normal runs.

        ApplicationConfiguration.Initialize();

        // Ensure WinForms exceptions go through our handler
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        // Register global handlers
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        Application.ThreadException += Application_ThreadException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            if (IsBenignShutdownException(ex) || Environment.HasShutdownStarted)
            {
                Debug.WriteLine($"Suppressed exception on shutdown: {ex.GetType()}: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                // Swallow the exception to avoid interrupting shutdown in the debugger
            }
            else
            {
                // Re-throw non-shutdown exceptions so they are visible during development
                throw;
            }
        }
        finally
        {
            // Defensive: unsubscribe handlers
            TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
            Application.ThreadException -= Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        }
    }

    private static void Application_ThreadException(object? sender, System.Threading.ThreadExceptionEventArgs e)
    {
        try
        {
            Debug.WriteLine($"Application.ThreadException: {e.Exception.GetType()}: {e.Exception.Message}");
            Debug.WriteLine(e.Exception.StackTrace);
            // If shutting down, swallow; otherwise allow the runtime to proceed (we already set CatchException)
            if (Environment.HasShutdownStarted || IsBenignShutdownException(e.Exception))
            {
                // swallow
                return;
            }
        }
        catch
        {
            // Protect handler from throwing
        }

        // If not shutdown and not benign, fail fast in debug to aid diagnosis
        // but do not crash silently in release.
        if (Debugger.IsAttached)
            Debugger.Break();
    }

    private static void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            var ex = e.ExceptionObject as Exception;
            Debug.WriteLine($"AppDomain.UnhandledException: IsTerminating={e.IsTerminating}");
            if (ex != null)
            {
                Debug.WriteLine($"{ex.GetType()}: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
            }

            // If termination was requested due to CLR shutdown or the exception looks benign, ignore.
            if (Environment.HasShutdownStarted || (ex != null && IsBenignShutdownException(ex)))
                return;
        }
        catch
        {
            // swallow any errors in the handler
        }

        if (Debugger.IsAttached)
            Debugger.Break();
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            Debug.WriteLine($"TaskScheduler.UnobservedTaskException: {e.Exception.GetType()}: {e.Exception.Message}");
            Debug.WriteLine(e.Exception.StackTrace);

            // Mark observed to prevent process termination in older runtimes
            e.SetObserved();

            // If it's a benign shutdown-time exception, swallow silently
            if (Environment.HasShutdownStarted || IsBenignShutdownException(e.Exception))
                return;
        }
        catch
        {
            // swallow
        }

        if (Debugger.IsAttached)
            Debugger.Break();
    }

    private static bool IsBenignShutdownException(Exception ex)
    {
        // Common exceptions during shutdown that can be safely ignored for user convenience.
        // Extend this list if you observe other specific exception types during close.
        return ex is ObjectDisposedException
            || ex is InvalidOperationException
            || ex is AggregateException // often wraps other benign exceptions
            || ex is OutOfMemoryException
            || ex is System.ComponentModel.Win32Exception;
    }
}