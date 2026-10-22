using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using G27Hub.App.Controls;
using static G27Hub.Core.Lang;

namespace G27Hub.App;

/// <summary>Traduz os textos fixos do XAML (Text, Content, ToolTip, Label, Run) andando pela árvore lógica.</summary>
public static partial class LangUi
{
    [GeneratedRegex(@"\p{L}{2}")]
    private static partial Regex HasWords();

    static string Tr(string s) => HasWords().IsMatch(s) ? T(s) : s;

    public static void Translate(DependencyObject root)
    {
        if (!English) return;
        Walk(root);
    }

    static void Walk(object o)
    {
        if (o is RoadTest) return; // monta os textos sozinho, já traduzidos
        switch (o)
        {
            case TextBlock tb when tb.Inlines.Count > 1 || tb.Inlines.FirstInline is Run { Name.Length: > 0 }:
                foreach (var run in tb.Inlines.OfType<Run>().ToList()) run.Text = Tr(run.Text);
                break;
            case TextBlock tb when System.Windows.Data.BindingOperations.GetBinding(tb, TextBlock.TextProperty) == null:
                tb.Text = Tr(tb.Text);
                break;
            case ContentControl { Content: string s } cc:
                cc.Content = Tr(s);
                break;
            case SettingSlider ss:
                ss.Label = Tr(ss.Label);
                break;
        }
        if (o is FrameworkElement { ToolTip: string tip } fe) fe.ToolTip = Tr(tip);
        if (o is Window w && w.Title is { } title) w.Title = Tr(title);
        if (o is DependencyObject d)
            foreach (var child in LogicalTreeHelper.GetChildren(d).Cast<object>().ToList()) Walk(child);
    }
}
