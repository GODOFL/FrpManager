using System.Windows;
using System.Windows.Threading;

namespace FrpManager
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (_, ex) =>
            {
                MessageBox.Show(
                    $"运行时错误：\n\n{ex.Exception.Message}\n\n{ex.Exception.StackTrace}",
                    "FrpManager 错误", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            {
                MessageBox.Show(ex.ExceptionObject?.ToString(),
                    "FrpManager 崩溃", MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }
    }
}
