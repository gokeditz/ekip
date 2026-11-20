namespace Ekip.Mobil;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Dark; // Masaüstü gibi hep koyu tema.
	}

	protected override Window CreateWindow(IActivationState? activationState) =>
		new(new GirisSayfasi()) { Title = Tema.UygulamaAdi };
}
