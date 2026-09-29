using Avalonia.Controls;

namespace EngelsizDPI.Views;

public partial class MainWindow : Window
{
    /// <summary>true olduğunda kapatma gerçekten uygulamadan çıkar; aksi halde pencere tepsiye gizlenir.</summary>
    public bool AllowClose { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (AllowClose) return;
            e.Cancel = true;
            Hide();
        };
    }
}
