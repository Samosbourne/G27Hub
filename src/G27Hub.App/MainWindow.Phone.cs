using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using G27Hub.Core;
using G27Hub.Logging;
using QRCoder;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

/// <summary>Painel no celular (Fase 10): liga/desliga, QR code com o endereço + código, e os comandos que vêm do celular.</summary>
public partial class MainWindow
{
    PhonePanel? _phone;
    volatile string _phoneProfiles = "[]"; // lista de perfis já em JSON (montada na thread da interface)

    void StartPhone()
    {
        if (!_config.PhoneOn) return;
        _config.PhoneToken ??= PhonePanel.NewToken();
        _config.Save();
        RefreshPhoneProfiles();
        _phone = new PhonePanel(_config.PhoneToken, PhoneState, (what, v) => Dispatcher.BeginInvoke(() => PhoneCommand(what, v)));
        if (!_phone.Start())
        {
            _phone = null;
            ShowToast("", "Error", T("Painel no celular não ligou"), T($"A porta {PhonePanel.Port} está ocupada por outro programa."), T("Ver detalhes"));
        }
        UpdatePhoneUi();
    }

    void StopPhone()
    {
        _phone?.Dispose();
        _phone = null;
        UpdatePhoneUi();
    }

    void PhoneCheck_Click(object sender, RoutedEventArgs e)
    {
        _config.PhoneOn = PhoneCheck.IsChecked == true;
        _config.Save();
        if (_config.PhoneOn) StartPhone(); else StopPhone();
    }

    /// <summary>Código novo: celulares com o QR antigo param de funcionar.</summary>
    void PhoneNewCode_Click(object sender, RoutedEventArgs e)
    {
        _config.PhoneToken = PhonePanel.NewToken();
        AppLog.Info("APP", "Painel no celular: código trocado");
        StopPhone();
        StartPhone();
    }

    void UpdatePhoneUi()
    {
        PhoneCheck.IsChecked = _config.PhoneOn;
        PhoneBox.Visibility = _phone != null ? Visibility.Visible : Visibility.Collapsed;
        if (_phone == null) return;
        var url = PhonePanel.Url(_config.PhoneToken!);
        PhoneUrl.Text = url;
        using var qr = new QRCodeGenerator().CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(qr).GetGraphic(8);
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = new MemoryStream(png);
        img.EndInit();
        PhoneQr.Source = img;
    }

    void RefreshPhoneProfiles() =>
        _phoneProfiles = JsonSerializer.Serialize(_config.Profiles.Select(p => new { id = p.Id, name = p.IsDesktop ? T("DESKTOP: Padrão") : p.Name }));

    /// <summary>Roda na thread do servidor: só lê valores simples.</summary>
    string PhoneState()
    {
        var f = _telemetry.Current;
        var w = W;
        var json = JsonSerializer.Serialize(new
        {
            lang = Current,
            connected = _dev.IsConnected,
            game = f?.Source,
            activeId = _config.ActiveId,
            range = w.Range,
            ranges = _config.RotationSteps,
            gain = w.MasterGain,
            rpm = f?.Rpm ?? 0,
            maxRpm = f?.MaxRpm ?? 0,
            gear = f?.Gear,
            speed = f?.Speed,
            leds = Math.Max(_ledMask, 0),
        });
        return json[..^1] + ",\"profiles\":" + _phoneProfiles + "}";
    }

    void PhoneCommand(string what, string value)
    {
        switch (what)
        {
            case "profile" when _config.Profiles.FirstOrDefault(p => p.Id == value) is { } p:
                ActivateProfile(p, auto: false);
                break;
            case "range" when int.TryParse(value, out var r):
                W.Range = Math.Clamp(r, 40, 900);
                _config.Save();
                LoadSettingsIntoUi();
                ApplyWheel(force: false);
                break;
            case "gain" when int.TryParse(value, out var g):
                W.MasterGain = Math.Clamp(g, 0, 100);
                _config.Save();
                LoadSettingsIntoUi();
                ApplyWheel(force: false);
                break;
            default:
                AppLog.Aviso("APP", $"Painel no celular: comando desconhecido {what}");
                break;
        }
    }
}
