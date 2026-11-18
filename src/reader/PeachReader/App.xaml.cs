namespace PeachReader;

/// <summary>The application: one window showing <see cref="MainPage"/>.</summary>
public partial class App : Application
{
    /// <summary>Creates the application.</summary>
    public App()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new MainPage()) { Title = "PeachReader", Width = 1100, Height = 760 };
}
