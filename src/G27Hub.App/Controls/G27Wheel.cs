using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using G27Hub.Core;

namespace G27Hub.App.Controls;

/// <summary>
/// Desenho próprio do G27 (vetor, 400x400, centro 200,200), feito a partir da foto do volante do John:
/// três raios de alumínio, cubo preto com 6 parafusos, 10 LEDs em arco, 3 botões vermelhos em cada lateral
/// e borboletas atrás. Gira com <see cref="Angle"/>; os botões acendem com <see cref="SetPressed"/>.
/// </summary>
public sealed class G27Wheel : UserControl
{
    readonly RotateTransform _rot = new(0, 200, 200);
    readonly Shape[] _leds = new Shape[10];
    readonly Dictionary<int, Shape> _buttons = []; // número do botão → forma no desenho
    readonly Canvas _hotspots = new() { Visibility = Visibility.Collapsed };

    public event Action<int>? HotspotClicked;

    public G27Wheel()
    {
        var c = new Canvas { Width = 400, Height = 400, RenderTransform = _rot };

        // borboletas (atrás de tudo)
        _buttons[G27Buttons.PaddleLeft] = c.At(Paddle(false), 0, 0);
        _buttons[G27Buttons.PaddleRight] = c.At(Paddle(true), 0, 0);

        // raios de alumínio escovado
        var metal = new LinearGradientBrush([new GradientStop(Color.FromRgb(0xC9, 0xC9, 0xC9), 0), new GradientStop(Color.FromRgb(0x9A, 0x9A, 0x9A), 0.5), new GradientStop(Color.FromRgb(0xB8, 0xB8, 0xB8), 1)], 90);
        foreach (var d in new[] { "M 60,164 L 160,150 L 160,250 L 60,226 Z", "M 340,164 L 240,150 L 240,250 L 340,226 Z", "M 172,262 L 228,262 L 236,372 L 164,372 Z" })
            c.Children.Add(new Path { Data = Geometry.Parse(d), Fill = metal, Stroke = Draw.Hex("#6A6A6A"), StrokeThickness = 1 });

        // aro de couro com costura
        c.At(Draw.Circle(356, Brushes.Transparent, Draw.Hex("#161616"), 38), 22, 22);
        c.At(Draw.Circle(296, Brushes.Transparent, Draw.Hex("#3A3A3A"), 1), 52, 52).StrokeDashArray = [2.5, 2];
        c.At(Draw.Circle(344, Brushes.Transparent, Draw.Hex("#3A3A3A"), 1), 28, 28).StrokeDashArray = [2.5, 2];

        // laterais cinza com 3 botões vermelhos cada (formato de seta, como no G27)
        foreach (var (x, nums, right) in new[] { (44.0, G27Buttons.WheelLeft, false), (326.0, G27Buttons.WheelRight, true) })
        {
            c.At(Draw.Box(30, 92, 6, Draw.Hex("#4A4A4C"), Draw.Hex("#2A2A2A"), 1.5), x, 150);
            for (int i = 0; i < 3; i++)
            {
                var b = new Path
                {
                    Data = Geometry.Parse(right ? "M 0,2 L 14,0 Q 20,7 14,14 L 0,12 Z" : "M 20,2 L 6,0 Q 0,7 6,14 L 20,12 Z"),
                    Fill = Draw.Hex("#D42A2A"), Stroke = Draw.Hex("#5A0A0A"), StrokeThickness = 1,
                };
                _buttons[nums[i]] = c.At(b, x + 5, 160 + i * 26);
            }
        }

        // cubo preto com parafusos e LEDs em arco
        c.Children.Add(new Path { Data = Geometry.Parse(ArcPath(200, 200, 76, 66, 114)), Stroke = Draw.Hex("#070707"), StrokeThickness = 16 });
        string[] colors = ["#35E06B", "#35E06B", "#FFC531", "#FF8A1F", "#FF3B3B"];
        for (int i = 0; i < 10; i++)
        {
            var a = (112 - i * 44.0 / 9) * Math.PI / 180;
            var color = colors[i < 5 ? i : 9 - i]; // espelhado: verde nas pontas, vermelho no meio
            _leds[i] = c.At(Draw.Circle(8, Draw.Hex(color)), 200 + 76 * Math.Cos(a) - 4, 200 - 76 * Math.Sin(a) - 4);
        }
        c.At(Draw.Circle(128, Draw.Hex("#121212"), Draw.Hex("#2C2C2C"), 1.5), 136, 136);
        for (int i = 0; i < 6; i++)
        {
            var a = (90 - i * 60) * Math.PI / 180;
            c.At(Draw.Circle(11, Draw.Hex("#8C8C8C"), Draw.Hex("#3A3A3A"), 1), 200 + 49 * Math.Cos(a) - 5.5, 200 - 49 * Math.Sin(a) - 5.5);
        }
        c.At(Draw.Circle(70, Draw.Hex("#050505"), Draw.Hex("#222"), 1), 165, 165);
        c.At(Draw.Label("G27", 14, Draw.Hex("#6E6E6E"), 70), 165, 191);

        // pontos clicáveis da aba Atribuições, com o número real do botão
        foreach (var (num, shape) in _buttons)
        {
            var center = Center(shape);
            var dot = new Grid { Width = 24, Height = 24, Cursor = Cursors.Hand, Tag = num };
            dot.Children.Add(new Ellipse { Fill = new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0)), Stroke = Draw.Hex("#1A9BFF"), StrokeThickness = 2 });
            dot.Children.Add(new TextBlock { Text = num.ToString(), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            dot.MouseLeftButtonUp += (_, _) => HotspotClicked?.Invoke(num);
            _hotspots.At(dot, center.X - 12 + (center.X < 200 ? -26 : 26), center.Y - 12);
        }
        c.Children.Add(_hotspots);

        Content = new Viewbox { Child = c };
        SetLeds(0);
    }

    static Path Paddle(bool right)
    {
        var d = "M 96,158 L 66,78 Q 70,58 92,60 L 132,146 Z";
        var p = new Path { Data = Geometry.Parse(d), Fill = Draw.Hex("#1B1B1B"), Stroke = Draw.Hex("#3A3A3A"), StrokeThickness = 1.5 };
        if (right) p.RenderTransform = new ScaleTransform(-1, 1, 200, 0);
        return p;
    }

    static string ArcPath(double cx, double cy, double r, double fromDeg, double toDeg)
    {
        Point P(double deg) => new(cx + r * Math.Cos(deg * Math.PI / 180), cy - r * Math.Sin(deg * Math.PI / 180));
        var a = P(fromDeg);
        var b = P(toDeg);
        return FormattableString.Invariant($"M {a.X},{a.Y} A {r},{r} 0 0 0 {b.X},{b.Y}");
    }

    static Point Center(Shape s)
    {
        var b = s is Path p ? p.Data.Bounds : new Rect(0, 0, s.Width, s.Height);
        var pt = new Point(Canvas.GetLeft(s) + b.X + b.Width / 2, Canvas.GetTop(s) + b.Y + b.Height / 2);
        return s.RenderTransform is ScaleTransform { ScaleX: -1 } ? new Point(400 - pt.X, pt.Y) : pt;
    }

    /// <summary>Ângulo do volante em graus (negativo = esquerda).</summary>
    public double Angle { get => _rot.Angle; set => _rot.Angle = value; }

    public bool ShowHotspots { get => _hotspots.Visibility == Visibility.Visible; set => _hotspots.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }

    /// <summary>Bit 0 = par de LEDs de fora (verde) … bit 4 = par do meio (vermelho).</summary>
    public void SetLeds(int mask)
    {
        for (int i = 0; i < 10; i++) _leds[i].Opacity = (mask >> (i < 5 ? i : 9 - i) & 1) != 0 ? 1 : 0.15;
    }

    /// <summary>Acende (borda azul) os botões apertados no volante físico.</summary>
    public void SetPressed(IReadOnlyCollection<int> pressed)
    {
        foreach (var (num, shape) in _buttons)
        {
            var on = pressed.Contains(num);
            shape.Stroke = on ? Draw.Hex("#1A9BFF") : num is G27Buttons.PaddleLeft or G27Buttons.PaddleRight ? Draw.Hex("#3A3A3A") : Draw.Hex("#5A0A0A");
            shape.StrokeThickness = on ? 3.5 : 1;
        }
    }

    /// <summary>Destaca o ponto escolhido na aba Atribuições.</summary>
    public void Highlight(int button)
    {
        foreach (Grid dot in _hotspots.Children)
            ((Ellipse)dot.Children[0]).Fill = (int)dot.Tag == button ? Draw.Hex("#1A9BFF") : new SolidColorBrush(Color.FromArgb(0xCC, 0, 0, 0));
    }
}
