using System.Numerics;
using G27Hub.Core;

namespace G27Hub.Core.Tests;

public class VibrationTests
{
    static TelemetryFrame Frame(float rpm = 3000, int gear = 3, float speed = 80, Vector3? accel = null, float abs = 0) =>
        new("teste", rpm, 6000, gear, Speed: speed, Abs: abs, Accel: accel ?? new Vector3(0, 9.81f, 0));

    [Fact]
    public void Desligada_ou_sem_telemetria_nao_vibra()
    {
        var m = new VibrationMixer();
        Assert.Equal(Vibration.None, m.Step(Frame(), new WheelSettings()));            // VibOn = false de fábrica
        Assert.Equal(Vibration.None, m.Step(null, new WheelSettings { VibOn = true }));
    }

    [Fact]
    public void Motor_pista_abs_marcha_e_batida()
    {
        var w = new WheelSettings { VibOn = true };
        var m = new VibrationMixer();

        var v = m.Step(Frame(), w);
        Assert.True(v.EngineAmp > 0 && v.EngineHz is >= 8 and <= 45);
        Assert.Equal(0, v.KickMs);

        // estrada lisa: sem vibração de pista; zebra (vertical sacudindo): vibra
        for (int i = 0; i < 20; i++) v = m.Step(Frame(), w);
        Assert.Equal(0, v.RoadAmp);
        for (int i = 0; i < 20; i++) v = m.Step(Frame(accel: new Vector3(0, 9.81f + (i % 2 == 0 ? 8 : -8), 0)), w);
        Assert.True(v.RoadAmp > 1000);

        // ABS agindo: pulso de 16 Hz
        v = m.Step(Frame(abs: 1), w);
        Assert.Equal(16, v.RoadHz);
        Assert.True(v.RoadAmp > 0);

        // troca de marcha: tranco curto; neutro não conta
        v = m.Step(Frame(gear: 4), w);
        Assert.Equal(60, v.KickMs);
        Assert.Equal(0, m.Step(Frame(gear: 0), w).KickMs);

        // mesmo quadro de novo: não repete o tranco
        var same = Frame(gear: 5);
        Assert.Equal(60, m.Step(same, w).KickMs);
        Assert.Equal(0, m.Step(same, w).KickMs);

        // batida: salto de 6 g na lateral
        v = m.Step(Frame(gear: 5, accel: new Vector3(60, 9.81f, 0)), w);
        Assert.Equal(150, v.KickMs);
        Assert.True(Math.Abs(v.KickAmp) > 2000);
    }
}
