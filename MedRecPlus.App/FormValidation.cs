using System.Globalization;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MedRecPlus.App;

/// <summary>Keeps field messages beside their inputs, including fields created in code.</summary>
internal sealed class FormValidation
{
    private sealed record Field(Control Input, TextBlock Message, Func<string?> Check, string HelpText);
    private readonly List<Field> _fields = [];
    private bool _submitted;

    public static TextBlock CreateMessage() => new()
    {
        Style = (Style)Application.Current.Resources["FieldErrorStyle"],
    };

    public void Add(Control input, TextBlock message, Func<string?> check)
    {
        var field = new Field(input, message, check, AutomationProperties.GetHelpText(input));
        _fields.Add(field);

        void Changed() { if (_submitted) Update(field, check()); }
        switch (input)
        {
            case TextBox text: text.TextChanged += (_, _) => Changed(); break;
            case PasswordBox password: password.PasswordChanged += (_, _) => Changed(); break;
            case ComboBox combo: combo.SelectionChanged += (_, _) => Changed(); break;
            case NumberBox number:
                number.ValueChanged += (_, _) => Changed();
                TextBox? subscribedEditor = null;
                TextChangedEventHandler onTextChanged = (_, _) => Changed();
                number.Loaded += (_, _) =>
                {
                    if (Descendant<TextBox>(number) is { } editor && editor != subscribedEditor)
                    {
                        // NumberBox.VerticalContentAlignment does not consistently reach its template editor.
                        // Center the actual TextBox content so the displayed value aligns with other inputs.
                        editor.VerticalAlignment = VerticalAlignment.Stretch;
                        editor.VerticalContentAlignment = VerticalAlignment.Center;
                        if (subscribedEditor != null) subscribedEditor.TextChanged -= onTextChanged;
                        subscribedEditor = editor;
                        editor.TextChanged += onTextChanged;
                    }
                };
                break;
        }
    }

    public bool Validate()
    {
        _submitted = true;
        foreach (var field in _fields) Update(field, field.Check());
        var first = _fields.FirstOrDefault(field => field.Message.Visibility == Visibility.Visible);
        if (first == null) return true;
        Focus(first.Input);
        return false;
    }

    public void Recheck(Control? input = null)
    {
        if (_submitted)
            foreach (var field in _fields.Where(field => input == null || field.Input == input))
                Update(field, field.Check());
    }

    public void Show(Control input, string message)
    {
        _submitted = true;
        Update(_fields.Single(field => field.Input == input), message);
        Focus(input);
    }

    public void Reset()
    {
        _submitted = false;
        foreach (var field in _fields) Update(field, null);
    }

    private static void Update(Field field, string? error)
    {
        field.Message.Text = error ?? "";
        field.Message.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetHelpText(field.Input, error ?? field.HelpText);
    }

    private static void Focus(Control input)
    {
        input.Focus(FocusState.Programmatic);
        // Bring the whole field and its message into view in long, scrollable dialogs.
        (input.Parent as FrameworkElement)?.StartBringIntoView();
    }

    public static string? Required(string value, string label) =>
        string.IsNullOrWhiteSpace(value) ? $"{label} is required." : null;

    public static string? Password(string value)
    {
        if (string.IsNullOrEmpty(value)) return "Password is required.";
        try { PasswordService.ValidatePolicy(value); return null; }
        catch (ArgumentException ex) { return ex.Message; }
    }

    // NumberBox.Value may still hold the previous value while its editor has focus.
    public static double Number(NumberBox input)
    {
        var text = Descendant<TextBox>(input)?.Text ?? input.Text;
        if (input.NumberFormatter is Windows.Globalization.NumberFormatting.INumberParser parser)
            return parser.ParseDouble(text) ?? double.NaN;
        return double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.CurrentCulture, out var value) ? value : double.NaN;
    }

    private static T? Descendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (Descendant<T>(child) is { } nested) return nested;
        }
        return null;
    }
}
