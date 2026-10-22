using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using static G27Hub.Core.Lang;

namespace G27Hub.App.Controls;

/// <summary>
/// Minijogo de teste do force feedback em 3D (WPF 3D, sem dependências). Dois modos:
/// automático (roteiro fixo de ~27 s: zebra, lombadas, grama, terra, batida) e livre (pista infinita,
/// o John dirige com volante e pedais). <see cref="Step"/> avança o tempo e devolve a força para o volante
/// em % (positivo empurra para a esquerda, como o G27).
/// </summary>
public sealed class RoadTest : Grid
{
    const double HalfWidth = 5;     // meia largura do asfalto
    const double Curb = 1.2;        // largura da zebra
    const double SampleStep = 2;    // m entre pontos da pista
    const double ChunkLength = 120; // m de pista por bloco 3D
    const double DemoSpeed = 24;    // m/s (~86 km/h)
    const double CrashAt = 610;     // m (automático)

    record struct Sample(Point3D P, Vector3D Right, double Heading, double Curvature, string Floor);
    sealed class Feature(double s, string kind, double offset) { public double S = s; public string Kind = kind; public double Offset = offset; public bool Hit; }

    // Roteiro do automático: comprimento (m), curvatura (1/raio; + = direita), piso.
    static readonly (double Length, double Curvature, string Floor)[] DemoTrack =
    [
        (80, 0, "asfalto"), (150, 1 / 115.0, "asfalto"), (70, 0, "asfalto"),
        (150, -1 / 115.0, "asfalto"), (70, 0, "terra"), (180, 0, "asfalto"),
    ];

    // Onde o "piloto" do automático põe o carro (deslocamento lateral, + = direita).
    static readonly (double From, double To, double Offset)[] DemoLine =
    [
        (95, 205, 4.7),   // corta a curva por dentro, rodas da direita na zebra
        (330, 368, 5.6),  // abre na curva à esquerda, em cima da zebra de fora
        (375, 425, 9.5),  // sai da pista: grama
    ];

    readonly List<Sample> _path = [];
    readonly List<Feature> _features = [];
    readonly Queue<(double End, Model3DGroup Model)> _chunks = new();
    readonly Model3DGroup _world = new();
    readonly TranslateTransform3D _groundShift = new();
    readonly PerspectiveCamera _camera = new() { FieldOfView = 62, NearPlaneDistance = 0.3, FarPlaneDistance = 700 };
    readonly Transform3DGroup _carTransform = new();
    readonly TextBlock _label = new() { FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    readonly TextBlock _info = new() { FontSize = 15, Foreground = Brushes.White, Margin = new Thickness(0, 6, 0, 0) };
    readonly Rectangle _flash = new() { Fill = Brushes.Red, Opacity = 0, IsHitTestVisible = false };
    readonly Rectangle _progress = new() { Height = 4, Fill = new SolidColorBrush(Color.FromRgb(0x1A, 0x9B, 0xFF)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
    Random _rnd = new(27);

    double _s, _offset, _yaw, _speed, _curvature, _impulse, _shake, _grass, _bounce, _prevAngle, _crashTime, _built;
    bool _crashed;
    string _event = "";
    double _eventUntil;

    public bool Free { get; private set; }
    public double Time { get; private set; }
    public double Duration => CrashAt / DemoSpeed + 1.6;
    public bool Finished => !Free && _crashed && Time - _crashTime > 1.6;
    public string Floor { get; private set; } = "asfalto";
    public double Distance => _s;

    /// <summary>Metade da rotação configurada (graus até o batente); no livre, o batente = esterço máximo.</summary>
    public double WheelLock { get; set; } = 450;

    public RoadTest()
    {
        ClipToBounds = true;
        Background = new LinearGradientBrush(Color.FromRgb(0x2E, 0x5C, 0x96), Color.FromRgb(0xB8, 0xD4, 0xEE), 90);

        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(0x70, 0x70, 0x70)));
        scene.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-0.4, -1, -0.6)));
        var ground = new MeshGeometry3D();
        Quad(ground, new Point3D(-900, -0.05, 900), new Point3D(900, -0.05, 900), new Point3D(900, -0.05, -900), new Point3D(-900, -0.05, -900));
        scene.Children.Add(new GeometryModel3D(ground, new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x2F, 0x6E, 0x2C)))) { Transform = _groundShift });
        scene.Children.Add(_world);
        var car = BuildCar();
        car.Transform = _carTransform;
        scene.Children.Add(car);

        Children.Add(new Viewport3D { Camera = _camera, Children = { new ModelVisual3D { Content = scene } } });
        Children.Add(_flash);
        Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), CornerRadius = new CornerRadius(6), Padding = new Thickness(18, 10, 18, 12),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(24),
            Child = new StackPanel { Children = { _label, _info } },
        });
        Children.Add(_progress);
        Reset(0, free: false);
    }

    public void Reset(double wheelAngle, bool free)
    {
        Free = free;
        Time = _s = _offset = _yaw = _curvature = _impulse = _shake = _grass = _bounce = _crashTime = _built = 0;
        _speed = free ? 0 : DemoSpeed;
        _crashed = false;
        _event = "";
        _prevAngle = wheelAngle;
        _rnd = new Random(free ? Environment.TickCount : 27);
        _path.Clear();
        _features.Clear();
        _chunks.Clear();
        _world.Children.Clear();

        if (free) { Append(60, 0, "asfalto"); Generate(); }
        else
        {
            foreach (var (length, curvature, floor) in DemoTrack) Append(length, curvature, floor);
            _features.Add(new(262, "lombada", 0));
            _features.Add(new(282, "lombada", 0));
            _features.Add(new(CrashAt, "barreira", 0));
        }
        BuildChunks();
        Place();
    }

    /// <summary>Avança dt segundos e devolve a força em % (-100..100, positivo empurra para a esquerda).</summary>
    public double Step(double dt, double wheelAngle, double throttle = 0, double brake = 0)
    {
        Time += dt;
        var velocity = dt > 0 ? (wheelAngle - _prevAngle) / dt : 0;
        _prevAngle = wheelAngle;
        var prevS = _s;

        if (Free) Drive(dt, wheelAngle, throttle, brake);
        else if (!_crashed)
        {
            _s += _speed * dt;
            var want = DemoLine.Where(l => _s >= l.From && _s <= l.To).Select(l => l.Offset).DefaultIfEmpty(0).First();
            _offset += (want - _offset) * Math.Min(1, dt * 1.6);
        }

        var lookahead = At(_s + 12);
        _curvature += (lookahead.Curvature - _curvature) * Math.Min(1, dt * 3);
        var outer = Math.Abs(_offset) + 0.9;
        Floor = outer > HalfWidth + Curb ? "grama" : outer > HalfWidth ? "zebra" : At(_s).Floor;

        HitFeatures(prevS);

        // Força "da direção": no automático o volante segue a pista sozinho (ganhos medidos no G27 do John);
        // no livre é a força de alinhamento, mais firme quanto mais rápido, mais leve se o carro escorrega.
        double spring;
        if (Free)
        {
            var k = 0.1 + Math.Min(_speed, 40) / 40 * 0.45; // mais firme quanto mais rápido, sem brigar com a mão
            var sliding = Math.Abs(_yaw) > 0.25 ? 0.4 : 1;
            spring = Math.Clamp(wheelAngle * k * sliding + velocity * 0.06, -45, 45);
        }
        else
        {
            var target = Math.Clamp(_crashed ? 0 : _curvature * 10500, -120, 120);
            spring = Math.Clamp(-(target - wheelAngle) * 1.2 + velocity * 0.12, -60, 60);
        }

        var speedFactor = Free ? Math.Min(1, _speed / 15) : 1;
        double effect = 0;
        if (!_crashed && _speed > 0.5)
            switch (Floor)
            {
                case "zebra": // trepidação forte; frequência acompanha a velocidade
                    effect = ((int)(_s / 0.7) % 2 == 0 ? 60 : -60) * speedFactor;
                    _bounce = Math.Max(_bounce, 0.05 * speedFactor);
                    break;
                case "grama": // ondulações largas e irregulares + buracos
                    _grass = _grass * 0.85 + (_rnd.NextDouble() - 0.5) * 30;
                    effect = (_grass * 2.2 + (_rnd.NextDouble() < 0.03 ? (_rnd.Next(2) * 2 - 1) * 70 : 0)) * speedFactor;
                    _bounce = Math.Max(_bounce, Math.Abs(_grass) * 0.012 * speedFactor);
                    break;
                case "terra": // áspero e rápido
                    effect = ((_rnd.NextDouble() - 0.5) * 70 + ((int)(_s / 0.5) % 2 == 0 ? 15 : -15)) * speedFactor;
                    _bounce = Math.Max(_bounce, 0.04 * speedFactor);
                    break;
                default:
                    effect = (_rnd.NextDouble() - 0.5) * 4 * speedFactor; // textura leve do asfalto
                    break;
            }
        if (Math.Abs(_impulse) > 1)
        {
            effect += _impulse * ((int)(Time * 14) % 2 == 0 ? 1 : -1); // pancada que vai e volta
            _impulse *= Math.Pow(0.03, dt);
        }
        _shake *= Math.Pow(0.08, dt);
        _bounce *= Math.Pow(0.002, dt);

        if (Free && _crashed && Time - _crashTime > 1.2) Recover();
        if (Free && _path.Count * SampleStep - _s < 450) { Generate(); BuildChunks(); } // pista infinita
        while (_chunks.Count > 0 && _chunks.Peek().End < _s - 60) _world.Children.Remove(_chunks.Dequeue().Model);
        Place();

        // O tremor cabe no que sobra até 100%, igual para os dois lados; cortar só um lado empurraria o volante.
        var room = 100 - Math.Abs(spring);
        if (Math.Abs(effect) > room) effect *= room / Math.Abs(effect);
        return spring + effect;
    }

    // ---------------- modo livre: física simples ----------------

    void Drive(double dt, double wheelAngle, double throttle, double brake)
    {
        if (_crashed) { _speed = 0; return; }
        var drag = 0.4 + _speed * 0.012 + Floor switch { "grama" => 3.5, "terra" => 1.2, _ => 0 };
        _speed = Math.Clamp(_speed + (throttle * 7 - brake * 14 - drag) * dt, 0, 40);

        // Arcade: esterço máximo já com 270° de volante (mesmo em 900°), aderência alta e o carro
        // se alinha devagar com a pista quando o volante volta ao centro.
        var steer = Math.Clamp(wheelAngle / Math.Min(WheelLock, 270), -1, 1) * 30 * Math.PI / 180;
        var yawRate = _speed * Math.Tan(steer) / 2.6;
        if (_speed > 1) yawRate = Math.Clamp(yawRate, -16 / _speed, 16 / _speed); // limite de aderência: sai de frente
        _yaw = Math.Clamp(_yaw + (yawRate - _speed * At(_s).Curvature) * dt, -1.2, 1.2);
        _yaw *= Math.Pow(0.55, dt);
        _offset += _speed * Math.Sin(_yaw) * dt;
        _s += _speed * Math.Cos(_yaw) * dt;

        if (Math.Abs(_offset) > HalfWidth + 24) Crash(T("MURO! Voltando para a pista…"), 100);
    }

    void Crash(string text, double impulse)
    {
        _crashed = true;
        _crashTime = Time;
        _impulse = impulse;
        _shake = 1;
        Flash(text, 1.5);
    }

    void Recover()
    {
        _crashed = false;
        _offset = _yaw = 0;
        _speed = 5;
    }

    void Flash(string text, double seconds) { _event = text; _eventUntil = Time + seconds; }

    void HitFeatures(double prevS)
    {
        foreach (var f in _features)
        {
            if (f.Hit || f.S <= prevS || f.S > _s) continue;
            switch (f.Kind)
            {
                case "lombada":
                    f.Hit = true;
                    _impulse = 100 * Math.Min(1, Free ? _speed / 15 : 1);
                    _bounce = 0.35;
                    Flash(T("LOMBADA!"), 1);
                    break;
                case "buraco" when Math.Abs(_offset - 0.9 - f.Offset) < 0.7 || Math.Abs(_offset + 0.9 - f.Offset) < 0.7:
                    f.Hit = true;
                    _impulse = (f.Offset < _offset ? 1 : -1) * 85 * Math.Min(1, Free ? _speed / 12 : 1); // puxa para o lado do buraco
                    _bounce = 0.3;
                    Flash(T("BURACO!"), 1);
                    break;
                case "cone" when Math.Abs(_offset - f.Offset) < 1.3:
                    f.Hit = true;
                    _impulse = 75;
                    _shake = 0.6;
                    _speed *= 0.85;
                    Flash(T("ACERTOU UM CONE!"), 1);
                    break;
                case "barreira":
                    f.Hit = true;
                    _s = f.S - 2.2;
                    Crash(T("BATIDA!"), 100);
                    break;
            }
        }
    }

    // ---------------- pista ----------------

    void Append(double length, double curvature, string floor)
    {
        var last = _path.Count > 0 ? _path[^1] : new Sample(new Point3D(0, 0, 0), new Vector3D(1, 0, 0), 0, 0, floor);
        double x = last.P.X, z = last.P.Z, heading = last.Heading;
        for (double d = 0; d < length; d += SampleStep)
        {
            if (_path.Count > 0)
            {
                heading += curvature * SampleStep;
                x += Math.Sin(heading) * SampleStep;
                z -= Math.Cos(heading) * SampleStep;
            }
            _path.Add(new(new Point3D(x, 0, z), new Vector3D(Math.Cos(heading), 0, Math.Sin(heading)), heading, curvature, floor));
        }
    }

    /// <summary>Livre: acrescenta trechos aleatórios com obstáculos até ~600 m à frente.</summary>
    void Generate()
    {
        while (_path.Count * SampleStep < _s + 600)
        {
            var start = _path.Count * SampleStep;
            var length = 60 + _rnd.Next(8) * 20;
            var curvature = _rnd.NextDouble() < 0.4 ? 0 : (_rnd.Next(2) * 2 - 1) / (110 + _rnd.NextDouble() * 200); // raio 110–310 m
            var floor = _rnd.NextDouble() < 0.15 ? "terra" : "asfalto";
            Append(length, curvature, floor);

            if (_rnd.NextDouble() < 0.35)
                for (int i = 0; i < 1 + _rnd.Next(2); i++) _features.Add(new(start + 15 + _rnd.NextDouble() * (length - 30), "lombada", 0));
            for (int i = 0; i < _rnd.Next(4); i++)
                _features.Add(new(start + 10 + _rnd.NextDouble() * (length - 20), "buraco", (_rnd.NextDouble() - 0.5) * 2 * (HalfWidth - 1)));
            if (_rnd.NextDouble() < 0.3) // zigue-zague de cones
                for (int i = 0; i < 4; i++) _features.Add(new(start + 20 + i * 12, "cone", i % 2 == 0 ? -2 : 2));
        }
    }

    Sample At(double s)
    {
        var i = Math.Clamp(s / SampleStep, 0, _path.Count - 1.001);
        var a = _path[(int)i];
        var b = _path[(int)i + 1];
        var t = i - (int)i;
        return new(a.P + (b.P - a.P) * t, a.Right * (1 - t) + b.Right * t, a.Heading + (b.Heading - a.Heading) * t, a.Curvature, a.Floor);
    }

    // ---------------- cena ----------------

    void Place()
    {
        var p = At(_s);
        var pos = p.P + p.Right * _offset;
        var heading = p.Heading + _yaw;
        var y = _bounce * Math.Sin(Time * 60) + _shake * Math.Sin(Time * 47) * 0.4;

        _carTransform.Children.Clear();
        _carTransform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), _shake * Math.Sin(Time * 33) * 8)));
        _carTransform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), -heading * 180 / Math.PI)));
        _carTransform.Children.Add(new TranslateTransform3D(pos.X, y, pos.Z));
        _groundShift.OffsetX = Math.Round(pos.X / 100) * 100; // chão infinito acompanha o carro
        _groundShift.OffsetZ = Math.Round(pos.Z / 100) * 100;

        var camHeading = p.Heading + _yaw * 0.6;
        var forward = new Vector3D(Math.Sin(camHeading), 0, -Math.Cos(camHeading));
        var shakeSide = p.Right * (_shake * Math.Sin(Time * 71) * 0.5);
        _camera.Position = pos - forward * 8.5 + new Vector3D(0, 3 + y * 0.6, 0) + shakeSide;
        _camera.LookDirection = (pos + forward * 10 + new Vector3D(0, 0.6, 0)) - _camera.Position;
        _camera.UpDirection = new Vector3D(0, 1, 0);

        _label.Text = Time < _eventUntil ? _event : Free ? T("DIREÇÃO LIVRE") : DemoLabel;
        _info.Text = Free
            ? T($"{_speed * 3.6:0} km/h · piso: {T(Floor)} · {_s / 1000:0.0} km · acelerador e freio nos pedais")
            : T($"{(_crashed ? 0 : _speed * 3.6):0} km/h · piso: {(_crashed ? "—" : T(Floor))}");
        _flash.Opacity = _shake * 0.45;
        _progress.Visibility = Free ? Visibility.Collapsed : Visibility.Visible;
        _progress.Width = ActualWidth * Math.Min(Time / Duration, 1);
    }

    string DemoLabel => _crashed ? T("BATIDA!") : _s switch
    {
        < 80 => T("RETA"),
        < 230 => T("CURVA À DIREITA — cortando a zebra"),
        < 300 => T("LOMBADAS"),
        < 370 => T("CURVA À ESQUERDA — abrindo na zebra"),
        < 430 => T("SAIU DA PISTA — grama!"),
        < 450 => T("VOLTANDO PARA A PISTA"),
        < 520 => T("TRECHO DE TERRA"),
        < 580 => T("RETA"),
        _ => T("CUIDADO COM A BARREIRA…"),
    };

    sealed class Meshes
    {
        readonly Dictionary<Color, MeshGeometry3D> _byColor = [];
        public MeshGeometry3D this[Color c] => _byColor.TryGetValue(c, out var m) ? m : _byColor[c] = new MeshGeometry3D();

        public Model3DGroup Build()
        {
            var group = new Model3DGroup();
            foreach (var (color, mesh) in _byColor)
            {
                mesh.Freeze();
                var mat = new DiffuseMaterial(new SolidColorBrush(color));
                mat.Freeze();
                group.Children.Add(new GeometryModel3D(mesh, mat) { BackMaterial = mat });
            }
            return group;
        }
    }

    static void Quad(MeshGeometry3D m, Point3D a, Point3D b, Point3D c, Point3D d)
    {
        var i = m.Positions.Count;
        m.Positions.Add(a); m.Positions.Add(b); m.Positions.Add(c); m.Positions.Add(d);
        m.TriangleIndices.Add(i); m.TriangleIndices.Add(i + 1); m.TriangleIndices.Add(i + 2);
        m.TriangleIndices.Add(i); m.TriangleIndices.Add(i + 2); m.TriangleIndices.Add(i + 3);
    }

    /// <summary>Caixa alinhada ao trecho da pista: centro, eixo "direita", meia largura, altura, meia profundidade.</summary>
    static void Box(MeshGeometry3D m, Point3D c, Vector3D right, double halfW, double height, double halfD, double baseY = 0)
    {
        var fwd = Vector3D.CrossProduct(right, new Vector3D(0, 1, 0));
        Point3D P(double r, double y, double f) => c + right * r + new Vector3D(0, baseY + y, 0) + fwd * f;
        Point3D[] v = [P(-halfW, 0, -halfD), P(halfW, 0, -halfD), P(halfW, 0, halfD), P(-halfW, 0, halfD),
                       P(-halfW, height, -halfD), P(halfW, height, -halfD), P(halfW, height, halfD), P(-halfW, height, halfD)];
        Quad(m, v[4], v[5], v[6], v[7]);
        Quad(m, v[0], v[1], v[5], v[4]);
        Quad(m, v[1], v[2], v[6], v[5]);
        Quad(m, v[2], v[3], v[7], v[6]);
        Quad(m, v[3], v[0], v[4], v[7]);
    }

    static readonly Color Asphalt = Color.FromRgb(0x3C, 0x3E, 0x44), Asphalt2 = Color.FromRgb(0x42, 0x44, 0x4A);
    static readonly Color Dirt = Color.FromRgb(0x8A, 0x6A, 0x44), Dirt2 = Color.FromRgb(0x7C, 0x5E, 0x3B);
    static readonly Color GrassA = Color.FromRgb(0x3E, 0x8E, 0x3A), GrassB = Color.FromRgb(0x36, 0x80, 0x33);
    static readonly Color KerbRed = Color.FromRgb(0xD6, 0x26, 0x26), KerbWhite = Color.FromRgb(0xF2, 0xF2, 0xF2);
    static readonly Color Cone = Color.FromRgb(0xFF, 0x7A, 0x1A);

    /// <summary>Monta em 3D os blocos de pista ainda não construídos (um Model3DGroup por bloco, para descartar depois).</summary>
    void BuildChunks()
    {
        var end = (_path.Count - 1) * SampleStep;
        while (_built < end - 0.1)
        {
            var from = _built;
            var to = Math.Min(from + ChunkLength, end);
            var m = new Meshes();
            var i0 = (int)(from / SampleStep);
            var i1 = (int)(to / SampleStep);
            for (int i = i0; i < i1; i++) Segment(m, i);
            foreach (var f in _features.Where(f => f.S >= from && f.S < to)) FeatureModel(m, f);
            var model = m.Build();
            _world.Children.Add(model);
            _chunks.Enqueue((to, model));
            _built = to;
        }
    }

    void Segment(Meshes m, int i)
    {
        var a = _path[i];
        var b = _path[i + 1];
        var even = i / 2 % 2 == 0;
        Point3D L(Sample p, double off, double y = 0) => p.P + p.Right * off + new Vector3D(0, y, 0);

        var road = a.Floor == "terra" ? (even ? Dirt : Dirt2) : (even ? Asphalt : Asphalt2);
        Quad(m[road], L(a, -HalfWidth, 0.02), L(a, HalfWidth, 0.02), L(b, HalfWidth, 0.02), L(b, -HalfWidth, 0.02));
        if (a.Floor != "terra" && i % 4 < 2) // faixa central tracejada
            Quad(m[KerbWhite], L(a, -0.12, 0.03), L(a, 0.12, 0.03), L(b, 0.12, 0.03), L(b, -0.12, 0.03));
        foreach (var side in new[] { -1, 1 })
        {
            Quad(m[even ? KerbRed : KerbWhite], L(a, side * HalfWidth, 0.04), L(a, side * (HalfWidth + Curb), 0.04), L(b, side * (HalfWidth + Curb), 0.04), L(b, side * HalfWidth, 0.04));
            Quad(m[i / 3 % 2 == 0 ? GrassA : GrassB], L(a, side * (HalfWidth + Curb), 0.01), L(a, side * 60, 0.01), L(b, side * 60, 0.01), L(b, side * (HalfWidth + Curb), 0.01));
        }

        var s = i * SampleStep;
        if (s % 30 == 0) // cones na beira
            foreach (var side in new[] { -1, 1 })
                Box(m[Cone], L(a, side * (HalfWidth + Curb + 2)), a.Right, 0.25, 0.7, 0.25);
        if (s % 16 == 0) // árvores: tronco + copa
        {
            var side = _rnd.Next(2) * 2 - 1;
            var at = L(a, side * (HalfWidth + 10 + _rnd.NextDouble() * 25));
            Box(m[Color.FromRgb(0x5A, 0x3D, 0x22)], at, a.Right, 0.3, 2.2, 0.3);
            Box(m[Color.FromRgb(0x1E, 0x5E, 0x22)], at, a.Right, 1.8, 3.2, 1.8, 2.2);
        }
    }

    void FeatureModel(Meshes m, Feature f)
    {
        var p = At(f.S);
        switch (f.Kind)
        {
            case "lombada": // faixas amarelas e pretas
                for (int k = 0; k < 10; k++)
                    Box(m[k % 2 == 0 ? Color.FromRgb(0xF2, 0xC2, 0x1A) : Color.FromRgb(0x1A, 0x1A, 0x1A)], p.P + p.Right * (-HalfWidth + 0.5 + k), p.Right, 0.5, 0.14, 0.45);
                break;
            case "buraco":
                Box(m[Color.FromRgb(0x14, 0x12, 0x10)], p.P + p.Right * f.Offset, p.Right, 0.6, 0.035, 0.55);
                break;
            case "cone":
                Box(m[Cone], p.P + p.Right * f.Offset, p.Right, 0.32, 0.9, 0.32);
                Box(m[KerbWhite], p.P + p.Right * f.Offset, p.Right, 0.34, 0.12, 0.34, 0.45);
                break;
            case "barreira":
                for (int k = 0; k < 14; k++)
                    Box(m[k % 2 == 0 ? Color.FromRgb(0xFF, 0x4A, 0x1A) : KerbWhite], p.P + p.Right * (-HalfWidth - 1.8 + k * 0.97), p.Right, 0.49, 1.3, 0.5);
                break;
        }
    }

    static Model3DGroup BuildCar()
    {
        var m = new Meshes();
        var r = new Vector3D(1, 0, 0);
        var o = new Point3D(0, 0, 0);
        Box(m[Color.FromRgb(0x1A, 0x9B, 0xFF)], o, r, 1.0, 0.55, 2.2, 0.3);                                 // carroceria
        Box(m[Color.FromRgb(0x0E, 0x2A, 0x44)], o + new Vector3D(0, 0, 0.3), r, 0.75, 0.45, 1.0, 0.85);    // cabine
        Box(m[Color.FromRgb(0x10, 0x10, 0x10)], o + new Vector3D(0, 0, 2.1), r, 0.95, 0.12, 0.15, 0.95);   // aerofólio
        foreach (var x in new[] { -1.0, 1.0 })
            foreach (var z in new[] { -1.4, 1.5 })
                Box(m[Color.FromRgb(0x14, 0x14, 0x14)], o + new Vector3D(x, 0, z), r, 0.22, 0.62, 0.34);   // rodas
        Box(m[Color.FromRgb(0xC0, 0x10, 0x10)], o + new Vector3D(-0.65, 0, 2.21), r, 0.22, 0.12, 0.02, 0.55); // lanternas
        Box(m[Color.FromRgb(0xC0, 0x10, 0x10)], o + new Vector3D(0.65, 0, 2.21), r, 0.22, 0.12, 0.02, 0.55);
        return m.Build();
    }
}
