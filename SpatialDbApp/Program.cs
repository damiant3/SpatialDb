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
        finally
        {
            // Unsubscribe handlers
            TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;
            Application.ThreadException -= Application_ThreadException;
            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
        }
    }

    private static void Application_ThreadException(object? sender, System.Threading.ThreadExceptionEventArgs e)
    {
        // Log and let serious exceptions surface. If shutting down, still log and return.
        Debug.WriteLine($"Application.ThreadException: {e.Exception.GetType()}: {e.Exception.Message}");
        Debug.WriteLine(e.Exception.StackTrace);

        if (Environment.HasShutdownStarted || IsBenignShutdownException(e.Exception))
        {
            return;
        }

        if (Debugger.IsAttached)
            Debugger.Break();
    }

    private static void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Debug.WriteLine($"AppDomain.UnhandledException: IsTerminating={e.IsTerminating}");
        if (ex != null)
        {
            Debug.WriteLine($"{ex.GetType()}: {ex.Message}");
            Debug.WriteLine(ex.StackTrace);
        }

        // If termination was requested due to CLR shutdown or the exception looks benign, still return after logging
        if (Environment.HasShutdownStarted || (ex != null && IsBenignShutdownException(ex)))
            return;

        if (Debugger.IsAttached)
            Debugger.Break();
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Debug.WriteLine($"TaskScheduler.UnobservedTaskException: {e.Exception.GetType()}: {e.Exception.Message}");
        Debug.WriteLine(e.Exception.StackTrace);

        // Mark observed to prevent process termination in older runtimes
        e.SetObserved();

        if (Environment.HasShutdownStarted || IsBenignShutdownException(e.Exception))
            return;

        if (Debugger.IsAttached)
            Debugger.Break();
    }

    private static bool IsBenignShutdownException(Exception ex)
    {
        return ex is ObjectDisposedException
            || ex is InvalidOperationException
            || ex is AggregateException
            || ex is OutOfMemoryException
            || ex is System.ComponentModel.Win32Exception;
    }
}