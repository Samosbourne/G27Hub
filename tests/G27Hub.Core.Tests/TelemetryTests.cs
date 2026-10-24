using G27Hub.Core;
using static System.Buffers.Binary.BinaryPrimitives;

namespace G27Hub.Core.Tests;

public class TelemetryTests
{
    static readonly LedSettings Default = new();
    static TelemetryFrame Rpm(float pct, bool shift = false) => new("teste", pct * 70, 7000, 3, ShiftLight: shift);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(74, 0)]
    [InlineData(75, 0b1)]
    [InlineData(84, 0b11)]
    [InlineData(90, 0b1111)]
    public void Progressivo_enche_pelos_limites(float pct, int mask) =>
        Assert.Equal(mask, RpmLeds.Mask(Rpm(pct), Default, nowMs: 0));

    [Fact]
    public void Progressivo_pisca_no_ponto_de_troca()
    {
        Assert.Equal(RpmLeds.All, RpmLeds.Mask(Rpm(96), Default, 0));
        Assert.Equal(0, RpmLeds.Mask(Rpm(96), Default, 80));
    }

    [Fact]
    public void Ponto_de_troca_so_acende_na_troca_e_aceita_a_luz_do_jogo()
    {
        var s = new LedSettings { Mode = LedMode.ShiftPoint };
        Assert.Equal(0, RpmLeds.Mask(Rpm(90), s, 0));
        Assert.Equal(RpmLeds.All, RpmLeds.Mask(Rpm(96), s, 0));
        Assert.Equal(RpmLeds.All, RpmLeds.Mask(Rpm(60, shift: true), s, 0));
        s.GameControls = false;
        Assert.Equal(0, RpmLeds.Mask(Rpm(60, shift: true), s, 0));
    }

    [Fact]
    public void Limitador_fica_aceso_e_so_pisca_no_corte()
    {
        var s = new LedSettings { Mode = LedMode.Limiter };
        Assert.Equal(RpmLeds.All, RpmLeds.Mask(Rpm(96), s, 80));
        Assert.Equal(0, RpmLeds.Mask(Rpm(99.5f), s, 80));
    }

    [Fact]
    public void Limitador_de_box_e_combustivel()
    {
        var pit = new TelemetryFrame("t", 3000, 7000, 1, PitLimiter: true);
        Assert.Equal(0b10101, RpmLeds.Mask(pit, Default, 0));
        Assert.Equal(0b01010, RpmLeds.Mask(pit, Default, 250));
        var fuel = new TelemetryFrame("t", 0, 7000, 1, Fuel: 0.02f);
        Assert.Equal(0, RpmLeds.Mask(fuel, Default, 0));
        Assert.Equal(1, RpmLeds.Mask(fuel, new LedSettings { LowFuelBlink = true }, 0));
    }

    [Fact]
    public void Barra_do_jogo_vale_quando_nao_ha_rpm_maxima()
    {
        var f1 = new TelemetryFrame("F1", 11000, 0, 5, GamePercent: 0.86f);
        Assert.Equal(0b111, RpmLeds.Mask(f1, new LedSettings { GameControls = false }, 0));
    }

    [Fact]
    public void Le_o_pacote_do_mod_do_BeamNG()
    {
        var b = new byte[28];
        "G27H"u8.CopyTo(b);
        WriteSingleLittleEndian(b.AsSpan(4), 5500);
        WriteSingleLittleEndian(b.AsSpan(8), 7200);
        WriteInt32LittleEndian(b.AsSpan(16), -1);
        WriteInt32LittleEndian(b.AsSpan(20), 1);
        WriteSingleLittleEndian(b.AsSpan(24), 0.5f);
        var f = TelemetryHub.HubPacket(b)!;
        Assert.Equal((5500f, 7200f, -1, true, 0.5f, "BeamNG.drive"), (f.Rpm, f.MaxRpm, f.Gear, f.ShiftLight, f.Fuel, f.Source));
        Assert.Null(TelemetryHub.HubPacket(new byte[28])); // carro ainda carregando: pacote zerado
        b[3] = (byte)'T'; // plugin do ETS2/ATS
        Assert.Equal("ETS2 / ATS", TelemetryHub.HubPacket(b)!.Source);
    }

    [Fact]
    public void Pacote_novo_traz_velocidade_abs_e_aceleracao()
    {
        var b = new byte[48];
        "G27T"u8.CopyTo(b);
        WriteSingleLittleEndian(b.AsSpan(4), 1200);
        WriteSingleLittleEndian(b.AsSpan(28), 25);    // m/s
        WriteSingleLittleEndian(b.AsSpan(32), 1);     // ABS
        WriteSingleLittleEndian(b.AsSpan(40), 9.81f); // vertical
        var f = TelemetryHub.HubPacket(b)!;
        Assert.Equal(90, f.Speed!.Value, 3);
        Assert.Equal(1, f.Abs);
        Assert.Equal(9.81f, f.Accel!.Value.Y);
        Assert.Null(TelemetryHub.HubPacket(b[..28])!.Accel); // mod/plugin antigo
    }

    [Fact]
    public void OutGauge_aprende_a_rpm_maxima_por_carro()
    {
        var b = new byte[96];
        "beam"u8.CopyTo(b.AsSpan(4));
        b[10] = 3; // 2ª
        WriteSingleLittleEndian(b.AsSpan(16), 6000);
        float max = 0; var car = "";
        TelemetryHub.OutGauge(b, ref max, ref car);
        WriteSingleLittleEndian(b.AsSpan(16), 3000);
        WriteUInt32LittleEndian(b.AsSpan(44), 1 | 8);
        var f = TelemetryHub.OutGauge(b, ref max, ref car)!;
        Assert.Equal((3000f, 6000f, 2, true, true), (f.Rpm, f.MaxRpm, f.Gear, f.ShiftLight, f.PitLimiter));
        "LFS1"u8.CopyTo(b.AsSpan(4));
        Assert.Equal(3000f, TelemetryHub.OutGauge(b, ref max, ref car)!.MaxRpm);
    }

    [Fact]
    public void Forza_le_rpm_e_ignora_menus()
    {
        var b = new byte[324];
        WriteSingleLittleEndian(b.AsSpan(8), 8000);
        WriteSingleLittleEndian(b.AsSpan(16), 4000);
        Assert.Null(TelemetryHub.Forza(b));
        WriteInt32LittleEndian(b, 1);
        Assert.Equal((4000f, 8000f), (TelemetryHub.Forza(b)!.Rpm, TelemetryHub.Forza(b)!.MaxRpm));
    }

    [Theory]
    [InlineData(2025, 29, 6, 27)]
    [InlineData(2022, 24, 5, 22)]
    public void F1_le_o_carro_do_jogador(int format, int header, int idAt, int playerAt)
    {
        var b = new byte[header + 22 * 60];
        WriteUInt16LittleEndian(b, (ushort)format);
        b[idAt] = 6;
        b[playerAt] = 2;
        int car = header + 2 * 60;
        b[car + 15] = 4;
        WriteUInt16LittleEndian(b.AsSpan(car + 16), 10500);
        b[car + 19] = 80;
        var f = TelemetryHub.F1(b)!;
        Assert.Equal((10500f, 4, 0.8f), (f.Rpm, f.Gear, f.GamePercent));
        b[idAt] = 7;
        Assert.Null(TelemetryHub.F1(b));
    }

    [Fact]
    public void Mod_do_BeamNG_instala_e_remove()
    {
        var dir = Path.Combine(Path.GetTempPath(), "g27hub-teste-" + Guid.NewGuid().ToString("N"));
        BeamNgTelemetry.Install(dir);
        Assert.True(BeamNgTelemetry.IsInstalled(dir));
        Assert.Contains("getPort() return 4445", File.ReadAllText(Path.Combine(dir, "lua", "vehicle", "protocols", "g27hub.lua")));
        BeamNgTelemetry.Uninstall(dir);
        Assert.False(BeamNgTelemetry.IsInstalled(dir));
    }

    [Fact]
    public void Plugin_do_ETS2_instala_e_remove()
    {
        var bin = Path.Combine(Path.GetTempPath(), "g27hub-teste-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bin);
        var dll = Path.Combine(bin, "fonte.dll");
        File.WriteAllText(dll, "x");
        ScsTelemetry.Install([bin], dll);
        Assert.True(ScsTelemetry.IsInstalled([bin]));
        ScsTelemetry.Uninstall([bin]);
        Assert.False(ScsTelemetry.IsInstalled([bin]));
    }
}
