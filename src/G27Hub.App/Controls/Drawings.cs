using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace G27Hub.App.Controls;

/// <summary>Pinceis e formas comuns dos desenhos próprios (pedais, câmbio).</summary>
static class Draw
{
    public static readonly Brush Metal = new LinearGradientBrush(
        [new GradientStop(Color.FromRgb(0xDA, 0xDA, 0xDA), 0), new GradientStop(Color.FromRgb(0x92, 0x92, 0x92), 0.6), new GradientStop(Color.FromRgb(0xB4, 0xB4, 0xB4), 1)], 90);
    public static readonly Brush Plate = Hex("#141414"), Edge = Hex("#2D2D2D"), Groove = Hex("#3A3A3A"), Dim = Hex("#5E5E5E");

    public static SolidColorBrush Hex(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    public static T At<T>(this Canvas c, T e, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        c.Children.Add(e);
        return e;
    }

    public static Ellipse Circle(double d, Brush fill, Brush? stroke = null, double thickness = 1) =>
        new() { Width = d, Height = d, Fill = fill, Stroke = stroke, StrokeThickness = thickness };

    public static Rectangle Box(double w, double h, double r, Brush fill, Brush? stroke = null, double thickness = 1) =>
        new() { Width = w, Height = h, RadiusX = r, RadiusY = r, Fill = fill, Stroke = stroke, StrokeThickness = thickness };

    public static TextBlock Label(string text, double size, Brush fg, double width) =>
        new() { Text = text, FontSize = size, FontWeight = FontWeights.Bold, Foreground = fg, Width = width, TextAlignment = TextAlignment.Center };
}

/// <summary>Os três pedais do G27 (embreagem, freio, acelerador), desenho próprio.</summary>
public sealed class G27Pedals : UserControl
{
    readonly Rectangle[] _faces = new Rectangle[3];

    public G27Pedals()
    {
        var c = new Canvas { Width = 420, Height = 280 };
        c.Children.Add(new Path { Data = Geometry.Parse("M 26,272 L 394,272 L 374,176 L 46,176 Z"), Fill = Draw.Plate, Stroke = Draw.Edge, StrokeThickness = 1.5 });
        Pedal(c, 0, 62, 46, 90, 116, 3, 4);
        Pedal(c, 1, 166, 46, 90, 116, 3, 4);
        Pedal(c, 2, 284, 18, 74, 148, 2, 6);
        Content = new Viewbox { Child = c };
    }

    void Pedal(Canvas c, int i, double x, double y, double w, double h, int cols, int rows)
    {
        c.At(Draw.Box(14, 220 - (y + h - 12), 3, Draw.Hex("#262626")), x + w / 2 - 7, y + h - 12); // haste
        _faces[i] = c.At(Draw.Box(w, h, 12, Draw.Metal, Draw.Dim, 1), x, y);
        _faces[i].RenderTransformOrigin = new Point(0.5, 1);
        _faces[i].RenderTransform = new ScaleTransform(1, 1);
        double gx = (w - 28) / Math.Max(cols - 1, 1), gy = (h - 30) / Math.Max(rows - 1, 1);
        for (int r = 0; r < rows; r++)
            for (int col = 0; col < cols; col++)
                c.At(Draw.Circle(10, Draw.Hex("#2A2A2A")), x + 14 + col * gx - 5, y + 15 + r * gy - 5);
    }

    /// <summary>0 = embreagem, 1 = freio, 2 = acelerador.</summary>
    public void SetPressed(int pedal, bool pressed)
    {
        _faces[pedal].Stroke = pressed ? (Brush)FindResource("Accent") : Draw.Dim;
        _faces[pedal].StrokeThickness = pressed ? 3 : 1;
    }

    /// <summary>Animação do "Configurar volante": o pedal afunda (0 = solto, 1 = no fundo).</summary>
    public void SetDepth(int pedal, double depth)
    {
        ((ScaleTransform)_faces[pedal].RenderTransform).ScaleY = 1 - 0.3 * depth;
        SetPressed(pedal, depth > 0.05);
    }
}

/// <summary>Câmbio H do G27 com marcha atual e os botões da base.</summary>
public sealed class G27Shifter : UserControl
{
    static readonly (string Gear, double X, double Y)[] Gears =
        [("1", 70, 62), ("2", 70, 238), ("3", 140, 62), ("4", 140, 238), ("5", 210, 62), ("6", 210, 238), ("R", 280, 238)];

    readonly Dictionary<string, (Ellipse Dot, TextBlock Text)> _gears = [];
    readonly List<Shape> _buttons = [];
    readonly TextBlock _neutral;
    readonly Ellipse _knob = Draw.Circle(30, Draw.Hex("#E2E8F0"), Draw.Hex("#00D2FF"), 3);

    public G27Shifter()
    {
        var c = new Canvas { Width = 520, Height = 300 };

        // grade do H
        c.At(Draw.Box(320, 290, 18, Draw.Plate, Draw.Edge, 1.5), 0, 5);
        var gate = new Path
        {
            Stroke = Draw.Groove, StrokeThickness = 7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse("M 70,62 L 70,238 M 140,62 L 140,238 M 210,62 L 210,238 M 280,150 L 280,238 M 70,150 L 280,150"),
        };
        c.Children.Add(gate);
        foreach (var (gear, x, y) in Gears)
        {
            var dot = c.At(Draw.Circle(40, Draw.Hex("#1E1E1E"), Draw.Groove, 2), x - 20, y - 20);
            var text = c.At(Draw.Label(gear, 17, Draw.Hex("#8A8A8A"), 40), x - 20, y - 12);
            _gears[gear] = (dot, text);
        }
        _neutral = c.At(Draw.Label("N", 15, Draw.Hex("#8A8A8A"), 30), 160, 128);
        c.At(_knob, 0, 0).Visibility = Visibility.Collapsed;

        // base de botões como no G27 (foto do John): placa prateada de topo arredondado,
        // 4 pretos em losango em cima, direcional redondo no meio (LED verde ao lado), 4 vermelhos embaixo
        c.At(new Path { Data = Geometry.Parse("M 0,290 L 0,70 A 92,70 0 0 1 185,70 L 185,290 Z"), Fill = Draw.Hex("#0E0E0E"), Stroke = Draw.Edge, StrokeThickness = 1.5 }, 335, 5);
        c.At(new Path { Data = Geometry.Parse("M 0,265 L 0,62 A 80,58 0 0 1 161,62 L 161,265 Z"), Fill = Draw.Metal, Stroke = Draw.Dim }, 347, 17);
        var red = new List<Shape>();
        for (int i = 0; i < 4; i++)
            red.Add(c.At(Draw.Circle(26, Draw.Hex("#C42424"), Draw.Hex("#3A0505")), 354 + i * 38, 240));
        var dpadRing = c.At(Draw.Circle(62, Draw.Hex("#151515"), Draw.Dim), 396, 140);
        c.At(new Path { Data = Geometry.Parse("M 10,0 L 20,0 L 20,10 L 30,10 L 30,20 L 20,20 L 20,30 L 10,30 L 10,20 L 0,20 L 0,10 L 10,10 Z"), Fill = Draw.Hex("#3A3A3A") }, 412, 156);
        c.At(Draw.Circle(6, Draw.Hex("#35E06B")), 474, 150);
        // losango na ordem dos botões 1–4: baixo, esquerda, direita, cima (1 e 4 confirmados pelo John)
        var black = new List<Shape>();
        foreach (var (x, y) in new[] { (427, 108), (399, 80), (455, 80), (427, 52) })
            black.Add(c.At(Draw.Circle(26, Draw.Hex("#141414"), Draw.Dim), x - 13, y - 13));
        _buttons.AddRange(red);
        _buttons.Add(dpadRing);
        _buttons.AddRange(black);

        Content = new Viewbox { Child = c };
    }

    /// <summary>
    /// Animação do "Configurar volante": a manopla sai do neutro e entra na marcha ("1".."6", "R"); progress 0..1
    /// (metade do caminho pelo corredor do meio, metade para cima/baixo). Null esconde.
    /// </summary>
    public void ShowKnob(string? gear, double progress)
    {
        _knob.Visibility = gear == null ? Visibility.Collapsed : Visibility.Visible;
        if (gear == null) return;
        var (_, gx, gy) = Gears.First(g => g.Gear == gear);
        double x = 175, y = 150;
        if (progress < 0.5) x += (gx - 175) * progress * 2;
        else (x, y) = (gx, 150 + (gy - 150) * (progress - 0.5) * 2);
        Canvas.SetLeft(_knob, x - 15);
        Canvas.SetTop(_knob, y - 15);
        SetGear(progress >= 0.98 ? gear : "N");
    }

    /// <summary>"1".."6", "R" ou "N".</summary>
    public void SetGear(string gear)
    {
        foreach (var (g, (dot, text)) in _gears)
        {
            dot.Fill = g == gear ? (Brush)FindResource("Accent") : Draw.Hex("#1E1E1E");
            dot.Stroke = g == gear ? (Brush)FindResource("Accent") : Draw.Groove;
            text.Foreground = g == gear ? Brushes.White : Draw.Hex("#8A8A8A");
        }
        _neutral.Foreground = gear == "N" ? (Brush)FindResource("Accent") : Draw.Hex("#8A8A8A");
    }

    /// <summary>Índices 0–3 vermelhos, 4 direcional, 5–8 pretos.</summary>
    public void SetPressed(params int[] pressed)
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].Stroke = pressed.Contains(i) ? (Brush)FindResource("Accent") : i < 4 ? Draw.Hex("#3A0505") : Draw.Dim;
            _buttons[i].StrokeThickness = pressed.Contains(i) ? 3 : 1;
        }
    }
}
