using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace G27Hub.App.Controls;

public partial class SettingSlider : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(SettingSlider));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(SettingSlider), new(0.0));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(SettingSlider), new(100.0));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(SettingSlider),
        new FrameworkPropertyMetadata(50.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, e) => ((SettingSlider)d).OnValue((double)e.NewValue)));
    /// <summary>Unidade mostrada depois do número ("%" ou "°").</summary>
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string), typeof(SettingSlider),
        new("%", (d, _) => ((SettingSlider)d).ShowValue()));

    /// <summary>Bolinha colorida antes do nome (cor do LED); sem cor, não aparece.</summary>
    public static readonly DependencyProperty DotProperty = DependencyProperty.Register(nameof(Dot), typeof(Brush), typeof(SettingSlider));

    /// <summary>Valor mudou (arrastando o slider ou por código).</summary>
    public event Action<double>? ValueChanged;

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush? Dot { get => (Brush?)GetValue(DotProperty); set => SetValue(DotProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    public SettingSlider()
    {
        InitializeComponent();
        ShowValue();
    }

    void OnValue(double v)
    {
        ShowValue();
        ValueChanged?.Invoke(v);
    }

    void ShowValue()
    {
        if (ValueText != null) ValueText.Text = $"{Value:0}{Unit}";
    }
}
