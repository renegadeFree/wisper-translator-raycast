using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace WisperTranslator.App.Controls;

/// <summary>Slider con etichetta e valore sempre visibile ("16 px", "95%", "5 giorni").</summary>
public partial class SliderRow : UserControl
{
    public SliderRow()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    public string Label
    {
        get => LabelText.Text;
        set => LabelText.Text = value;
    }

    public double Minimum
    {
        get => InternalSlider.Minimum;
        set => InternalSlider.Minimum = value;
    }

    public double Maximum
    {
        get => InternalSlider.Maximum;
        set => InternalSlider.Maximum = value;
    }

    public double TickFrequency
    {
        get => InternalSlider.TickFrequency;
        set => InternalSlider.TickFrequency = value;
    }

    public double Value
    {
        get => InternalSlider.Value;
        set => InternalSlider.Value = value;
    }

    /// <summary>Formato del valore, es. "{0:0} px" oppure "{0:P0}".</summary>
    public string ValueFormat { get; set; } = "{0:0}";

    /// <summary>Testo mostrato al posto del valore quando è zero (es. "per sempre").</summary>
    public string ZeroText { get; set; } = string.Empty;

    public event EventHandler? ValueChanged;

    private void OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        Refresh();
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Refresh()
    {
        if (ValueText is null)
        {
            return;
        }

        ValueText.Text = Value <= 0 && ZeroText.Length > 0
            ? ZeroText
            : string.Format(CultureInfo.CurrentCulture, ValueFormat, Value);
    }
}
