using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using PrinterManager.App.ViewModels;

namespace PrinterManager.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel old) old.PropertyChanged -= OnViewModelPropertyChanged;
            if (e.NewValue is MainViewModel vm) vm.PropertyChanged += OnViewModelPropertyChanged;
        };
        Closed += (_, _) => (DataContext as MainViewModel)?.Detach();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.FlashAlert)) return;

        // Alerta visual: pisca a faixa e o botão da barra de tarefas (RF07).
        var blink = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(350)) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(4) };
        AlertBanner.BeginAnimation(OpacityProperty, blink);
        if (!IsActive) FlashTaskbar();
    }

    private void FlashTaskbar()
    {
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = new WindowInteropHelper(this).Handle,
            dwFlags = 0x3 | 0xC, // FLASHW_ALL | FLASHW_TIMERNOFG
            uCount = 5
        };
        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
}
