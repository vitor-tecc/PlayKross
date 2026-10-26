using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows;

namespace JKiller.App.Localization;

public sealed class Language : INotifyPropertyChanged
{
    private static readonly Dictionary<string, string> messages = LoadMessages();
    private static readonly string preferenceFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlayKross", "language.txt");
    public static Language Current { get; } = new();
    private string selected = ReadPreference();
    public string Selected
    {
        get => selected;
        set
        {
            selected = value == "en" ? "en" : "pt-BR";
            try { Directory.CreateDirectory(Path.GetDirectoryName(preferenceFile)!); File.WriteAllText(preferenceFile, selected); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            PropertyChanged?.Invoke(this, new(nameof(Selected)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public static string Text(string text)
    {
        if (Current.Selected == "en") return text;
        if (messages.TryGetValue(text, out var exact)) return exact;
        foreach (var (source, translation) in messages.Where(pair => pair.Key.Contains("{0}")))
        {
            var pattern = "^" + Regex.Replace(Regex.Escape(source), @"\\\{(\d+)}", match => $"(?<v{match.Groups[1].Value}>.*?)") + "$";
            var found = Regex.Match(text, pattern, RegexOptions.Singleline | RegexOptions.CultureInvariant);
            if (found.Success) return Regex.Replace(translation, @"\{(\d+)\}", match => Text(found.Groups["v" + match.Groups[1].Value].Value));
        }
        // Messages can include a known contextual prefix and a technical detail.
        foreach (var prefix in new[] { "Broadcast stopped: ", "The original capture source is unavailable: ", "Native capture safety guard refused the selected sources. ", "Selected audio is not initialized. " })
            if (text.StartsWith(prefix, StringComparison.Ordinal)) return Text(prefix) + Text(text[prefix.Length..]);
        if (text.Contains("; ", StringComparison.Ordinal)) return string.Join("; ", text.Split("; ").Select(Text));
        return text;
    }
    public static void Bind(DependencyObject target, DependencyProperty property, string text)
    {
        var binding = new MultiBinding { Converter = new TranslationConverter(), Mode = BindingMode.OneWay };
        binding.Bindings.Add(new Binding { Source = text });
        binding.Bindings.Add(new Binding(nameof(Selected)) { Source = Current });
        BindingOperations.SetBinding(target, property, binding);
    }
    private static Dictionary<string, string> LoadMessages()
    {
        using var stream = typeof(Language).Assembly.GetManifestResourceStream("PlayKross.pt-BR.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    private static string ReadPreference()
    {
        try { return File.ReadAllText(preferenceFile).Trim() == "en" ? "en" : "pt-BR"; }
        catch (IOException) { return "pt-BR"; }
        catch (UnauthorizedAccessException) { return "pt-BR"; }
    }
}

// A second binding to Selected refreshes text without changing the underlying model.
public sealed class TranslationConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => Language.Text(values[0] as string ?? "");
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
