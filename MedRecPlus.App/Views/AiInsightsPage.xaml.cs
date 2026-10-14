using System.Collections.ObjectModel;
using MedRecPlus.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace MedRecPlus.App.Views;

public sealed partial class AiInsightsPage : Page
{
    public ObservableCollection<KpiRow> Kpis { get; } = new();
    public ObservableCollection<EvRow> Evidence { get; } = new();

    public AiInsightsPage()
    {
        InitializeComponent();
        KpiList.ItemsSource = Kpis;
        EvidenceList.ItemsSource = Evidence;
    }

    private void Suggest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string q)
        {
            QuestionBox.Text = q;
            Ask(q);
        }
    }

    private void Ask_Click(object sender, RoutedEventArgs e) => Ask(QuestionBox.Text);
    private void QuestionBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) Ask(QuestionBox.Text);
    }

    private void Ask(string question)
    {
        if (string.IsNullOrWhiteSpace(question)) return;
        AiAnswer ans;
        try
        {
            ans = AppSession.Ai.Answer(question, AppSession.Actor);
        }
        catch (Exception ex)
        {
            ans = new AiAnswer
            {
                Text = "Sorry, I couldn't analyze that. " + ex.Message + " " + AiAnswer.Disclaimer,
            };
        }
        AnswerText.Text = ans.Text;
        Kpis.Clear();
        foreach (var (label, value) in ans.Kpis)
            Kpis.Add(new KpiRow { Label = label, Value = value });
        Evidence.Clear();
        foreach (var (source, recordId, summary) in ans.Evidence)
            Evidence.Add(new EvRow { Source = $"{source} · {recordId}", Summary = summary });
        AnswerCard.Visibility = Visibility.Visible;
    }

    public sealed class KpiRow
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public sealed class EvRow
    {
        public string Source { get; set; } = "";
        public string Summary { get; set; } = "";
    }
}
