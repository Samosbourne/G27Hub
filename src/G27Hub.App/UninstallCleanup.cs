using System.Windows;
using G27Hub.Core;
using G27Hub.Logging;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

/// <summary>
/// Fase 9: chamado pelo desinstalador ("--uninstall-cleanup"). Desfaz tudo o que o app mudou fora da própria pasta:
/// registro do force feedback, iniciar com o Windows, calibração dos pedais, mod do BeamNG, plugin do ETS2/ATS,
/// HidHide e, se o usuário quiser, os drivers da Compatibilidade Total. A correção de controles do BeamNG fica
/// (é o certo para o G27 sem LGS) e a pasta de configuração também (perfis do usuário).
/// </summary>
static class UninstallCleanup
{
    public static void Run()
    {
        Lang.Set(AppConfig.Load().Language);
        AppLog.Info("APP", "Desinstalando: desfazendo as mudanças do G27 Hub no PC");
        Step("FFB", FfbRegistration.Unregister);
        Step("APP", () => AutoStart.Enabled = false);
        Step("HID", PedalCalibration.Clear);
        Step("TELEMETRIA", () => { if (BeamNgTelemetry.IsInstalled()) BeamNgTelemetry.Uninstall(); });
        Step("TELEMETRIA", () => ScsTelemetry.Uninstall(ScsTelemetry.GameDirs()));
        if (CompatMode.VJoy.IsInstalled() || CompatMode.HidHide.IsInstalled())
        {
            Step("VJOY", HidHide.ShowG27);
            if (MessageBox.Show(T("Remover também os drivers da Compatibilidade Total (vJoy e HidHide)?\n\nO Windows vai pedir permissão de administrador e, no fim, é preciso reiniciar o PC."),
                    "G27 Hub", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                Step("MODO", () => CompatMode.RunElevated("desinstalar", CompatMode.UninstallScript));
        }
        AppLog.Ok("APP", "Desinstalação: mudanças desfeitas");
    }

    static void Step(string origin, Action a)
    {
        try { a(); }
        catch (Exception ex) { AppLog.Erro(origin, "Desinstalação: passo falhou (seguindo)", ex); }
    }
}
