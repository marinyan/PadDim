using System.Globalization;

namespace PadDim;

public sealed class MinutesInput : NumericUpDown
{
    public MinutesInput()
    {
        Minimum = 10m / 60m;
        Maximum = 1440;
        Increment = 1;
        DecimalPlaces = 2;
        Width = 120;
    }

    public void CommitEdit() => ValidateEditText();

    protected override void UpdateEditText()
    {
        // Preserve NumericUpDown's culture-aware parsing, bounds, and overflow handling.
        if (UserEdit) ParseEditText();
        string formatted = Value.ToString("0.##", CultureInfo.CurrentCulture);
        if (Text == formatted) return;
        ChangingText = true;
        Text = formatted;
    }
}
