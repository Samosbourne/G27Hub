using System.Numerics;

namespace G27Hub.Core;

/// <summary>O que a g27ffb.dll deve somar à força do jogo. Amplitudes em -10000..10000 (100% = força máxima do G27).</summary>
public readonly record struct Vibration(int EngineAmp, float EngineHz, int RoadAmp, float RoadHz, int KickAmp, int KickMs)
{
    public static readonly Vibration None = default;
}

/// <summary>
/// Vibração extra pela telemetria (Fase 10): motor, pista/zebra, ABS, troca de marcha e batidas. Um quadro de telemetria
/// por chamada (~60 Hz); a DLL faz a onda a 500 Hz com esses números. Sem telemetria = nada.
/// </summary>
public sealed class VibrationMixer
{
    TelemetryFrame? _last;
    Vector3? _prevAccel;
    float _vertLow, _roadEnergy;
    int? _prevGear;
    bool _kickSign;
    Vibration _out;

    // ponytail: limites escolhidos de ouvido/mão; o ajuste fino é pelos sliders de cada efeito
    const int EngineMax = 1200, RoadMax = 3000, AbsMax = 3500, ShiftMax = 2500, ImpactMax = 8000;
    const float RoadFullScale = 6f;   // m/s² de "chacoalho" vertical que dá a vibração cheia da pista
    const float ImpactThreshold = 25f; // m/s² de mudança de um quadro para o outro (~2,5 g) = batida

    public Vibration Step(TelemetryFrame? f, WheelSettings w)
    {
        if (f == null || !w.VibOn)
        {
            _last = null;
            _prevAccel = null;
            _prevGear = null;
            _roadEnergy = 0;
            return _out = Vibration.None;
        }
        if (ReferenceEquals(f, _last)) return _out with { KickAmp = 0, KickMs = 0 }; // mesmo quadro: tranco já mandado
        _last = f;

        // motor: meia volta do virabrequim por ciclo, presa no que o G27 consegue tremer
        int engAmp = 0;
        float engHz = 0;
        if (f.Rpm > 100)
        {
            engHz = Math.Clamp(f.Rpm / 120f, 8, 45);
            var load = f.MaxRpm > 0 ? Math.Clamp(f.Rpm / f.MaxRpm, 0, 1) : 0.5f;
            engAmp = (int)(w.VibEngine / 100f * EngineMax * (0.5f + 0.5f * load));
        }

        // pista: o que sobra da aceleração vertical depois de tirar a parte lenta (lombada grande, subida) = textura/zebra
        int roadAmp = 0;
        float speed = f.Speed ?? 50;
        float roadHz = Math.Clamp(speed / 4, 10, 40);
        if (f.Accel is { } a && speed > 3)
        {
            _vertLow = _prevAccel == null ? a.Y : _vertLow + (a.Y - _vertLow) * 0.15f; // começa na gravidade, não em zero
            _roadEnergy = _roadEnergy * 0.8f + Math.Abs(a.Y - _vertLow) * 0.2f;
            // abaixo de 0,5 m/s² é ruído da simulação, não pista
            roadAmp = (int)(w.VibRoad / 100f * RoadMax * Math.Clamp((_roadEnergy - 0.5f) / RoadFullScale, 0, 1));
        }
        if (f.Abs > 0.1f && w.VibAbs > 0)
        {
            roadHz = 16;
            roadAmp = Math.Max(roadAmp, (int)(w.VibAbs / 100f * AbsMax));
        }

        // trancos: batida (salto grande na aceleração) vence a troca de marcha
        int kick = 0, kickMs = 0;
        if (f.Gear is { } g && _prevGear is { } pg && g != pg && g != 0 && w.VibShift > 0)
        {
            _kickSign = !_kickSign;
            kick = (int)(w.VibShift / 100f * ShiftMax) * (_kickSign ? 1 : -1);
            kickMs = 60;
        }
        if (f.Accel is { } acc && _prevAccel is { } pa && w.VibImpact > 0)
        {
            var d = acc - pa;
            var jump = d.Length();
            if (jump > ImpactThreshold)
            {
                kick = (int)(w.VibImpact / 100f * ImpactMax * Math.Clamp(jump / 80, 0.3f, 1)) * (d.X > 0 ? -1 : 1);
                kickMs = 150;
            }
        }
        _prevGear = f.Gear ?? _prevGear;
        _prevAccel = f.Accel;
        return _out = new(engAmp, engHz, roadAmp, roadHz, kick, kickMs);
    }
}
