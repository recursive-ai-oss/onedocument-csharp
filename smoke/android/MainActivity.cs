using System.Threading;
using Android.App;
using Android.OS;
using Android.Util;

namespace RecursiveAI.OneDocument.Smoke;

/// <summary>
/// Runs the shared checks when the app starts and writes each result line to the system log under the tag <c>OneDocumentSmoke</c>,
/// where the runner script reads them; then ends the process.
/// </summary>
[Activity(Label = "OneDocument smoke", MainLauncher = true)]
public sealed class MainActivity : Activity
{
    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // The UI thread must not be blocked, and layout and calc recurse deeply, so the work gets its own thread and a large stack.
        var worker = new Thread(
            () =>
            {
                int code = MobileRunner.Run(line => Log.Info("OneDocumentSmoke", line));
                Process.KillProcess(Process.MyPid());
                _ = code;
            },
            64 * 1024 * 1024);
        worker.Start();
    }
}
