using System.Threading;
using System.Windows;

namespace MoonDropEQ;

public partial class App : Application
{
    private static Mutex? _mutex;
    private static bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "MoonDropEQ.SingleInstance", out var created);
        _ownsMutex = created;
        if (!created)
        {
            MessageBox.Show("MoonDropEQ 已在运行中。", "MoonDropEQ", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "MoonDropEQ 出错了", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch
            {
                // 忽略释放异常
            }
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
